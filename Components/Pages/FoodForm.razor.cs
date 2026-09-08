using FridgeManager.Data.Enums;
using FridgeManager.Services;
using FridgeManager.Services.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace FridgeManager.Components.Pages;

public partial class FoodForm : IDisposable, IAsyncDisposable
{
    [Parameter]
    public int Id { get; set; }

    [Inject]
    private IInventoryService Inventory { get; set; } = default!;

    [Inject]
    private ICapacityService Capacity { get; set; } = default!;

    [Inject]
    private IFoodImageAnalysisService Analysis { get; set; } = default!;

    [Inject]
    private IOptions<OpenRouterOptions> OpenRouter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private FoodListState ListState { get; set; } = default!;

    [Inject]
    private IImageStorage ImageStorage { get; set; } = default!;

    [Inject]
    private UserClock Clock { get; set; } = default!;

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = default!;

    private readonly FoodItemForm Form = new();
    private readonly HashSet<string> _aiFields = new(StringComparer.Ordinal);
    private readonly HashSet<string> _userFilledFields = new(StringComparer.Ordinal);
    private EditForm? _editForm;
    private IReadOnlyList<ShelfUsageDto> _shelves = [];
    private IReadOnlyList<string> _aiWarnings = [];
    private UserUsageDto? _allowance;
    private ClaimsPrincipal _user = new();
    private bool _loading = true;
    private bool _saving;
    private bool _analyzing;
    private int _submitEpoch;
    private int _analyzeEpoch;
    private bool _originalActive;
    private int _originalShelfId;
    private int _originalSize;
    private string? _error;
    private string? _blocked;
    private string? _aiError;
    private IBrowserFile? _pendingFile;
    private byte[]? _pendingBytes;
    private string? _pendingContentType;
    private string? _previewUrl;
    private bool _previewFailed;
    private string? _photoError;
    private bool _dragging;
    private int _dragDepth;
    private bool _photoBusy;
    private int _photoEpoch;
    private CancellationTokenSource? _analyzeCts;
    private FormScroll? _scroll;
    private string? _scrollTarget;

    private bool IsEdit => Id > 0;

    private string? PhotoSrc
        => _previewFailed
            ? FoodDisplay.CategoryImage(Form.Category)
            : _previewUrl ?? ImageStorage.GetPublicUrl(Form.ImagePath);

    private string CancelHref => IsEdit ? $"food/{Id}" : ListState.LastListUrl;

    private ShelfUsageDto? SelectedShelf
        => _shelves.FirstOrDefault(s => s.ShelfId == Form.ShelfId);

    private int LiveRemaining => RemainingFor(SelectedShelf);

    private bool Insufficient => SelectedShelf is not null && LiveRemaining < Form.SizeUnits;

    private string ShelfInputClass => Insufficient ? "fm-input is-invalid" : "fm-input";

    private string SizeSegClass => IsAi(nameof(FoodItemForm.SizeUnits)) ? "fm-seg is-ai" : "fm-seg";

    private string ExpiryInputClass
        => IsAi(nameof(FoodItemForm.ExpirationDate)) ? "fm-input fig is-ai" : "fm-input fig";

    private bool ShowAi => !IsEdit && OpenRouter.Value.Enabled && _pendingBytes is not null;

    private bool FormBusy => _saving || _analyzing || _photoBusy;

    private bool PhotoLocked => FormBusy;

    private string PlatePickerClass
    {
        get
        {
            var classes = "form-plate form-plate-picker";
            if (_dragging)
            {
                classes += " is-dragging";
            }

            if (PhotoLocked)
            {
                classes += " is-busy";
            }

            if (_photoBusy)
            {
                classes += " is-pending";
            }

            return classes;
        }
    }

    private int UsedPercent
    {
        get
        {
            if (SelectedShelf is null || SelectedShelf.CapacityUnits == 0)
            {
                return 0;
            }

            var used = Math.Max(0, SelectedShelf.CapacityUnits - LiveRemaining);
            return (int)Math.Round(100.0 * used / SelectedShelf.CapacityUnits);
        }
    }

    private int AllowancePercent
        => _allowance is null || _allowance.Quota == 0
            ? 0
            : (int)Math.Round(100.0 * Math.Min(_allowance.Used, _allowance.Quota) / _allowance.Quota);

    private string RoomHint
    {
        get
        {
            var rooms = _shelves
                .Select(s => (s.Name, Free: RemainingFor(s)))
                .Where(x => x.Free >= Form.SizeUnits)
                .ToList();

            if (rooms.Count == 0)
            {
                return "Your entries are kept. No shelf currently has room for this size.";
            }

            var list = string.Join(", ", rooms.Select(r => $"{r.Name} has {r.Free} free"));
            return $"Your entries are kept. Choose a smaller size, or a shelf with room — {list}.";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _user = (await AuthState).User;
        await Clock.ResolveAsync(_user);
        await RefreshCapacityAsync();

        if (IsEdit)
        {
            var item = await Inventory.GetItemAsync(Id);
            if (item is null)
            {
                _blocked = "That item is not in the fridge.";
                _loading = false;
                return;
            }

            if (!UserClaims.CanModify(_user, item))
            {
                _blocked = "You can only edit your own items.";
                _loading = false;
                return;
            }

            var loaded = FoodItemForm.FromEntity(item);
            Form.Name = loaded.Name;
            Form.Category = loaded.Category;
            Form.ExpirationDate = loaded.ExpirationDate;
            Form.SizeUnits = loaded.SizeUnits;
            Form.IsShared = loaded.IsShared;
            Form.ShelfId = loaded.ShelfId;
            Form.PositionNote = loaded.PositionNote;
            Form.Note = loaded.Note;
            Form.ImagePath = loaded.ImagePath;
            _originalActive = item.Status == FoodStatus.Active;
            _originalShelfId = item.ShelfId;
            _originalSize = item.SizeUnits;
        }
        else
        {
            Form.ExpirationDate = Clock.Today;
            Form.SizeUnits = 1;
            Form.ShelfId = _shelves.FirstOrDefault()?.ShelfId ?? 0;
            if (OpenRouter.Value.Enabled)
            {
                _ = Analysis.WarmupAsync();
            }
        }

        _loading = false;
    }

    private async Task RefreshCapacityAsync()
    {
        _shelves = await Capacity.GetAllShelfUsageAsync();

        var userId = UserClaims.GetUserId(_user);
        if (!string.IsNullOrEmpty(userId))
        {
            _allowance = (await Capacity.GetAllUserUsageAsync())
                .FirstOrDefault(u => u.UserId == userId);
        }
    }

    private int RemainingFor(ShelfUsageDto? shelf)
    {
        if (shelf is null)
        {
            return 0;
        }

        var remaining = shelf.Remaining;
        if (IsEdit && _originalActive && _originalShelfId == shelf.ShelfId)
        {
            remaining += _originalSize;
        }

        return remaining;
    }

    private int NameLength => Form.Name?.Length ?? 0;

    private DateOnly Today => Clock.Today;

    private bool IsExpiryDays(int days) => Form.ExpirationDate == Today.AddDays(days);

    private bool IsExpiryMonths(int months) => Form.ExpirationDate == Today.AddMonths(months);

    private bool IsExpiryYears(int years) => Form.ExpirationDate == Today.AddYears(years);

    private static string ExpiryPresetClass(bool selected)
        => selected ? "fm-btn fm-btn-primary fm-btn-sm" : "fm-btn fm-btn-ghost fm-btn-sm";

    private void OnNameInput(ChangeEventArgs e)
    {
        Form.Name = e.Value?.ToString() ?? "";
        MarkUserFilled(nameof(FoodItemForm.Name));
        var context = _editForm?.EditContext;
        context?.NotifyFieldChanged(new FieldIdentifier(Form, nameof(FoodItemForm.Name)));
    }

    private void SetExpiryDays(int days)
    {
        Form.ExpirationDate = Today.AddDays(days);
        MarkUserFilled(nameof(FoodItemForm.ExpirationDate));
    }

    private void SetExpiryMonths(int months)
    {
        Form.ExpirationDate = Today.AddMonths(months);
        MarkUserFilled(nameof(FoodItemForm.ExpirationDate));
    }

    private void SetExpiryYears(int years)
    {
        Form.ExpirationDate = Today.AddYears(years);
        MarkUserFilled(nameof(FoodItemForm.ExpirationDate));
    }

    private void OnPhotoDragEnter()
    {
        if (PhotoLocked)
        {
            return;
        }

        _dragDepth++;
        _dragging = true;
    }

    private void OnPhotoDragLeave()
    {
        if (PhotoLocked)
        {
            _dragging = false;
            _dragDepth = 0;
            return;
        }

        _dragDepth = Math.Max(0, _dragDepth - 1);
        if (_dragDepth == 0)
        {
            _dragging = false;
        }
    }

    private async Task OnPhotoSelected(InputFileChangeEventArgs e)
    {
        var file = e.File;
        _previewFailed = false;
        _dragging = false;
        _dragDepth = 0;
        ClearAiState();

        if (!IsAllowedImageType(file.ContentType))
        {
            _photoError = "Use a JPG, PNG or WebP image.";
            _pendingFile = null;
            _pendingBytes = null;
            _pendingContentType = null;
            _previewUrl = null;
            _photoEpoch++;
            return;
        }

        _photoBusy = true;
        _photoError = null;
        await FlushBusyAsync();
        try
        {
            await using var stream = file.OpenReadStream(InventoryService.MaxImageBytes);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            _pendingBytes = bytes;
            _pendingContentType = file.ContentType;
            _pendingFile = new BufferedBrowserFile(file.Name, file.ContentType, bytes, file.LastModified);
            _previewUrl = CompactPreviewUrl(bytes, file.ContentType);
        }
        catch (IOException)
        {
            _photoError = "That file is larger than 5 MB.";
            _pendingFile = null;
            _pendingBytes = null;
            _pendingContentType = null;
            _previewUrl = null;
        }
        finally
        {
            _photoBusy = false;
            _photoEpoch++;
        }
    }

    private bool IsAi(string field) => _aiFields.Contains(field);

    private string FieldInputClass(string field)
        => IsAi(field) ? "fm-input is-ai" : "fm-input";

    private void MarkUserFilled(string field)
    {
        _aiFields.Remove(field);
        if (IsBlankUserField(field))
        {
            _userFilledFields.Remove(field);
            return;
        }

        _userFilledFields.Add(field);
    }

    private bool IsBlankUserField(string field) => field switch
    {
        nameof(FoodItemForm.Name) => string.IsNullOrWhiteSpace(Form.Name),
        nameof(FoodItemForm.Note) => string.IsNullOrWhiteSpace(Form.Note),
        _ => false
    };

    private void MarkUserCategory() => MarkUserFilled(nameof(FoodItemForm.Category));

    private void MarkUserSize() => MarkUserFilled(nameof(FoodItemForm.SizeUnits));

    private void MarkUserExpiry() => MarkUserFilled(nameof(FoodItemForm.ExpirationDate));

    private void MarkUserNote() => MarkUserFilled(nameof(FoodItemForm.Note));

    private void OnPhotoError()
    {
        if (_previewUrl is not null || !string.IsNullOrEmpty(Form.ImagePath))
        {
            _previewFailed = true;
        }
    }

    private static string CompactPreviewUrl(byte[] bytes, string contentType)
    {
        var preview = ImageNormalizer.Normalize(bytes, maxLongEdge: 800);
        if (preview is not null)
        {
            return $"data:{preview.ContentType};base64,{Convert.ToBase64String(preview.Bytes)}";
        }

        return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
    }

    private void ClearAiState()
    {
        _aiFields.Clear();
        _aiWarnings = [];
        _aiError = null;
    }

    private void OnInvalidSave()
    {
        _submitEpoch++;
        _scrollTarget = "invalid";
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_scrollTarget is null)
        {
            return;
        }

        _scroll ??= new FormScroll(JS);
        var target = _scrollTarget;
        _scrollTarget = null;
        if (target == "alert")
        {
            await _scroll.ToTopAsync();
        }
        else
        {
            await _scroll.ToInvalidAsync();
        }
    }

    private async Task FlushBusyAsync()
    {
        await InvokeAsync(StateHasChanged);
        await Task.Yield();
    }

    private async Task AnalyzeAsync()
    {
        if (FormBusy || _pendingBytes is null || _pendingContentType is null)
        {
            if (!_analyzing)
            {
                _analyzeEpoch++;
            }

            return;
        }

        _analyzing = true;
        _aiError = null;
        await FlushBusyAsync();
        try
        {
            _analyzeCts?.Dispose();
            _analyzeCts = new CancellationTokenSource();
            var result = await Analysis.AnalyzeAsync(
                _pendingBytes,
                _pendingContentType,
                _user,
                _analyzeCts.Token);
            if (!result.Success || result.Value is null)
            {
                _aiError = result.Error;
                return;
            }

            _aiFields.Clear();
            foreach (var field in result.Value.ApplyTo(Form, _userFilledFields))
            {
                _aiFields.Add(field);
            }

            _aiWarnings = result.Value.Warnings;
            NotifyAppliedFields();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _analyzing = false;
            _analyzeEpoch++;
        }
    }

    private void NotifyAppliedFields()
    {
        var context = _editForm?.EditContext;
        if (context is null)
        {
            return;
        }

        foreach (var field in _aiFields)
        {
            context.NotifyFieldChanged(new FieldIdentifier(Form, field));
        }
    }

    public void Dispose()
    {
        _analyzeCts?.Cancel();
        _analyzeCts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_scroll is not null)
        {
            await _scroll.DisposeAsync();
        }
    }

    private static bool IsAllowedImageType(string? contentType)
        => contentType is "image/jpeg" or "image/png" or "image/webp";

    private async Task SaveAsync()
    {
        if (FormBusy)
        {
            if (!_saving)
            {
                _submitEpoch++;
            }

            return;
        }

        _saving = true;
        _error = null;
        await FlushBusyAsync();
        try
        {
            var previousImagePath = Form.ImagePath;
            string? uploadedPath = null;
            if (_pendingFile is not null)
            {
                var uploaded = await Inventory.SaveImageAsync(_pendingFile, _user);
                if (!uploaded.Success || uploaded.Value is null)
                {
                    _error = uploaded.Error;
                    _scrollTarget = "alert";
                    await RefreshCapacityAsync();
                    ReleaseSave();
                    return;
                }

                uploadedPath = uploaded.Value;
                Form.ImagePath = uploadedPath;
            }

            if (IsEdit)
            {
                var result = await Inventory.UpdateItemAsync(Id, Form, _user);
                if (!result.Success)
                {
                    _error = result.Error;
                    _scrollTarget = "alert";
                    await RollbackUploadAsync(uploadedPath, previousImagePath);
                    return;
                }

                _pendingFile = null;
                Navigation.NavigateTo($"food/{Id}");
                return;
            }

            var created = await Inventory.CreateItemAsync(Form, _user);
            if (!created.Success || created.Value is null)
            {
                _error = created.Error;
                _scrollTarget = "alert";
                await RollbackUploadAsync(uploadedPath, previousImagePath);
                return;
            }

            _pendingFile = null;
            Navigation.NavigateTo($"food/{created.Value.Id}");
        }
        catch
        {
            ReleaseSave();
            throw;
        }
    }

    private async Task RollbackUploadAsync(string? uploadedPath, string? previousImagePath)
    {
        await Inventory.DeleteImageAsync(uploadedPath);
        if (uploadedPath is not null)
        {
            Form.ImagePath = previousImagePath;
        }

        await RefreshCapacityAsync();
        ReleaseSave();
    }

    private void ReleaseSave()
    {
        _saving = false;
        _submitEpoch++;
    }

    private sealed class BufferedBrowserFile : IBrowserFile
    {
        private readonly byte[] _bytes;

        public BufferedBrowserFile(string name, string contentType, byte[] bytes, DateTimeOffset lastModified)
        {
            Name = name;
            ContentType = contentType;
            _bytes = bytes;
            LastModified = lastModified;
        }

        public string Name { get; }
        public DateTimeOffset LastModified { get; }
        public long Size => _bytes.Length;
        public string ContentType { get; }

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (Size > maxAllowedSize)
            {
                throw new IOException(
                    $"Supplied file with size {Size} bytes exceeds the maximum of {maxAllowedSize} bytes.");
            }

            return new MemoryStream(_bytes, writable: false);
        }
    }
}
