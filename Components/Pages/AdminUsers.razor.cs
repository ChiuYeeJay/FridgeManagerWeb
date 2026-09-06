using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FridgeManager.Services;
using FridgeManager.Services.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace FridgeManager.Components.Pages;

public partial class AdminUsers
{
    [Inject]
    private IUserAdminService Admin { get; set; } = default!;

    [Inject]
    private ICapacityService Capacity { get; set; } = default!;

    [Inject]
    private UserClock Clock { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState> AuthState { get; set; } = default!;

    private readonly CreateMemberForm Create = new();
    private IReadOnlyList<AdminUserDto>? _users;
    private ClaimsPrincipal _actor = new();
    private string? _currentUserId;
    private string? _editingUserId;
    private string _editUserName = "";
    private string _editEmail = "";
    private int _editQuota;
    private bool _editInvalid;
    private bool _createOpen;
    private bool _showCreatePassword;
    private bool _disabledOpen;
    private bool _busy;
    private Alert? _alert;
    private int _quotaGranted;
    private int _held;
    private int _usedUnits;
    private int _totalCapacity;

    private IReadOnlyList<AdminUserDto> ActiveMembers
        => _users?.Where(u => u.IsActive).ToList() ?? [];

    private IReadOnlyList<AdminUserDto> DisabledMembers
        => _users?.Where(u => !u.IsActive).ToList() ?? [];

    private string MemberSummary
    {
        get
        {
            if (_users is null)
            {
                return "";
            }

            var active = ActiveMembers.Count;
            var disabled = DisabledMembers.Count;
            var activeLabel = active == 1 ? "One member active" : $"{active} members active";
            var disabledLabel = disabled switch
            {
                0 => "none disabled",
                1 => "one disabled",
                _ => $"{disabled} disabled"
            };
            return $"{activeLabel} · {disabledLabel}";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _actor = (await AuthState).User;
        _currentUserId = UserClaims.GetUserId(_actor);
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var users = await Admin.GetUsersAsync(_actor);
        if (!users.Success || users.Value is null)
        {
            _alert = new Alert("Could not load", users.Error ?? "Administrators only.");
            _users = [];
            return;
        }

        _users = users.Value;
        _quotaGranted = _users.Where(u => u.IsActive).Sum(u => u.Quota);
        _held = _users.Sum(u => u.ActiveCount);

        await Clock.ResolveAsync(_actor);
        var stats = await Capacity.GetDashboardStatsAsync(Clock.Today);
        _usedUnits = stats.UsedUnits;
        _totalCapacity = stats.TotalCapacity;
    }

    private void BeginEdit(AdminUserDto member)
    {
        _alert = null;
        _editInvalid = false;
        _editingUserId = member.UserId;
        _editUserName = member.UserName;
        _editEmail = member.Email;
        _editQuota = member.Quota;
    }

    private void CancelEdit()
    {
        _editingUserId = null;
        _editInvalid = false;
    }

    private void OnUserNameInput(ChangeEventArgs e)
        => _editUserName = e.Value?.ToString() ?? "";

    private void OnEmailInput(ChangeEventArgs e)
        => _editEmail = e.Value?.ToString() ?? "";

    private void OnQuotaInput(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var value))
        {
            _editQuota = value;
        }
    }

    private async Task SaveEditAsync()
    {
        if (_editingUserId is null)
        {
            return;
        }

        _busy = true;
        _alert = null;
        _editInvalid = false;
        try
        {
            var result = await Admin.UpdateMemberAsync(
                _editingUserId,
                _editUserName,
                _editEmail,
                _editQuota,
                _actor);
            if (!result.Success)
            {
                _editInvalid = true;
                _alert = new Alert("Could not update", result.Error ?? "Could not update this member.");
                return;
            }

            _editingUserId = null;
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SetActiveAsync(string userId, bool isActive)
    {
        _busy = true;
        _alert = null;
        try
        {
            var result = await Admin.SetActiveAsync(userId, isActive, _actor);
            if (!result.Success)
            {
                _alert = new Alert("Could not update", result.Error ?? "Could not update this member.");
                return;
            }

            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SetAdminAsync(string userId, bool isAdmin)
    {
        _busy = true;
        _alert = null;
        try
        {
            var result = await Admin.SetAdminAsync(userId, isAdmin, _actor);
            if (!result.Success)
            {
                _alert = new Alert("Could not update", result.Error ?? "Could not update this member.");
                return;
            }

            await LoadAsync();
            _alert = new Alert(
                "Role updated",
                "This member will have the new permission at their next sign-in.",
                Success: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ToggleDisabled() => _disabledOpen = !_disabledOpen;

    private void OpenCreate()
    {
        _alert = null;
        _showCreatePassword = false;
        _createOpen = true;
    }

    private void CloseCreate()
    {
        _createOpen = false;
        _showCreatePassword = false;
        Create.Reset();
    }

    private void ToggleCreatePassword() => _showCreatePassword = !_showCreatePassword;

    private async Task CreateAsync()
    {
        _busy = true;
        _alert = null;
        try
        {
            var result = await Admin.CreateUserAsync(
                Create.UserName,
                Create.Email,
                Create.Password,
                Create.Quota,
                Create.IsAdmin,
                _actor);
            if (!result.Success)
            {
                _alert = new Alert("Could not create", result.Error ?? "Could not create this member.");
                return;
            }

            CloseCreate();
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed record Alert(string Kicker, string Body, bool Success = false);

    private sealed class CreateMemberForm
    {
        [Required]
        [StringLength(64)]
        public string UserName { get; set; } = "";

        [Required]
        [EmailAddress]
        [StringLength(256)]
        public string Email { get; set; } = "";

        [Required]
        [StringLength(100, MinimumLength = 6)]
        public string Password { get; set; } = "";

        [Range(0, 999)]
        public int Quota { get; set; } = 5;

        public bool IsAdmin { get; set; }

        public void Reset()
        {
            UserName = "";
            Email = "";
            Password = "";
            Quota = 5;
            IsAdmin = false;
        }
    }
}
