using FridgeManager.Data.Enums;
using FridgeManager.Services;
using FridgeManager.Services.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace FridgeManager.Components.Pages;

public partial class FoodForm
{
    [Parameter]
    public int Id { get; set; }

    [Inject]
    private IInventoryService Inventory { get; set; } = default!;

    [Inject]
    private ICapacityService Capacity { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = default!;

    private readonly FoodItemForm Form = new();
    private IReadOnlyList<ShelfUsageDto> _shelves = [];
    private UserUsageDto? _allowance;
    private ClaimsPrincipal _user = new();
    private bool _loading = true;
    private bool _saving;
    private bool _originalActive;
    private int _originalShelfId;
    private int _originalSize;
    private string? _error;
    private string? _blocked;

    private bool IsEdit => Id > 0;

    private string CancelHref => IsEdit ? $"food/{Id}" : "food";

    private ShelfUsageDto? SelectedShelf
        => _shelves.FirstOrDefault(s => s.ShelfId == Form.ShelfId);

    private int LiveRemaining => RemainingFor(SelectedShelf);

    private bool Insufficient => SelectedShelf is not null && LiveRemaining < Form.SizeUnits;

    private string ShelfInputClass => Insufficient ? "fm-input is-invalid" : "fm-input";

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
        _shelves = await Capacity.GetAllShelfUsageAsync();

        var userId = UserClaims.GetUserId(_user);
        if (!string.IsNullOrEmpty(userId))
        {
            _allowance = (await Capacity.GetAllUserUsageAsync())
                .FirstOrDefault(u => u.UserId == userId);
        }

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
            Form.ExpirationDate = DateOnly.FromDateTime(DateTime.UtcNow);
            Form.SizeUnits = 1;
            Form.ShelfId = _shelves.FirstOrDefault()?.ShelfId ?? 0;
        }

        _loading = false;
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

    private async Task SaveAsync()
    {
        _saving = true;
        _error = null;
        try
        {
            if (IsEdit)
            {
                var result = await Inventory.UpdateItemAsync(Id, Form, _user);
                if (!result.Success)
                {
                    _error = result.Error;
                    return;
                }

                Navigation.NavigateTo($"food/{Id}");
                return;
            }

            var created = await Inventory.CreateItemAsync(Form, _user);
            if (!created.Success || created.Value is null)
            {
                _error = created.Error;
                return;
            }

            Navigation.NavigateTo($"food/{created.Value.Id}");
        }
        finally
        {
            _saving = false;
        }
    }
}
