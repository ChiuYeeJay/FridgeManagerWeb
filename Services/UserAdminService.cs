using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FridgeManager.Data;
using FridgeManager.Services.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Services;

public class UserAdminService(
    IDbContextFactory<AppDbContext> factory,
    UserManager<ApplicationUser> users)
    : IUserAdminService
{
    public const string AdministratorsOnly = "Administrators only.";
    public const string CannotRemoveOwnAdmin = "You cannot remove your own admin role.";

    public async Task<OperationResult<List<AdminUserDto>>> GetUsersAsync(ClaimsPrincipal actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!UserClaims.IsAdmin(actor))
        {
            return OperationResult<List<AdminUserDto>>.Fail(AdministratorsOnly);
        }

        await using var db = await factory.CreateDbContextAsync();
        var accounts = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var usedByOwner = await db.FoodItems
            .AsNoTracking()
            .Where(f => f.Status == Data.Enums.FoodStatus.Active)
            .GroupBy(f => f.OwnerId)
            .Select(g => new { OwnerId = g.Key, Used = g.Count() })
            .ToDictionaryAsync(x => x.OwnerId, x => x.Used);

        var admins = await users.GetUsersInRoleAsync("Admin");
        var adminIds = admins.Select(a => a.Id).ToHashSet();

        var rows = accounts
            .Select(u => new AdminUserDto(
                u.Id,
                u.UserName ?? "",
                u.Email ?? "",
                u.ItemQuota,
                usedByOwner.GetValueOrDefault(u.Id),
                u.IsActive,
                adminIds.Contains(u.Id)))
            .ToList();

        return OperationResult<List<AdminUserDto>>.Ok(rows);
    }

    public async Task<OperationResult> CreateUserAsync(
        string userName,
        string email,
        string password,
        int quota,
        bool isAdmin,
        ClaimsPrincipal actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!UserClaims.IsAdmin(actor))
        {
            return OperationResult.Fail(AdministratorsOnly);
        }

        userName = userName?.Trim() ?? "";
        email = email?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(userName))
        {
            return OperationResult.Fail("Username is required.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return OperationResult.Fail("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return OperationResult.Fail("Password is required.");
        }

        if (quota < 0)
        {
            return OperationResult.Fail("Quota cannot be negative.");
        }

        if (await users.FindByNameAsync(userName) is not null)
        {
            return OperationResult.Fail("That username is already in use.");
        }

        if (await users.FindByEmailAsync(email) is not null)
        {
            return OperationResult.Fail("That email is already in use.");
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            ItemQuota = quota,
            IsActive = true
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return OperationResult.Fail(FormatErrors(created));
        }

        var roleName = isAdmin ? "Admin" : "User";
        var role = await users.AddToRoleAsync(user, roleName);
        if (!role.Succeeded)
        {
            return OperationResult.Fail(FormatErrors(role));
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetActiveAsync(string userId, bool isActive, ClaimsPrincipal actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!UserClaims.IsAdmin(actor))
        {
            return OperationResult.Fail(AdministratorsOnly);
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        if (!isActive && user.Id == UserClaims.GetUserId(actor))
        {
            return OperationResult.Fail("You cannot disable your own account.");
        }

        if (user.IsActive == isActive)
        {
            return OperationResult.Ok();
        }

        user.IsActive = isActive;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return OperationResult.Fail(FormatErrors(updated));
        }

        if (!isActive)
        {
            await users.UpdateSecurityStampAsync(user);
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetQuotaAsync(string userId, int quota, ClaimsPrincipal actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!UserClaims.IsAdmin(actor))
        {
            return OperationResult.Fail(AdministratorsOnly);
        }

        if (quota < 0)
        {
            return OperationResult.Fail("Quota cannot be negative.");
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        await using var db = await factory.CreateDbContextAsync();
        var used = await CapacityQueries.UserUsageAsync(db, user.Id);
        if (quota < used)
        {
            var name = FoodDisplay.OwnerLabel(user);
            return OperationResult.Fail(
                $"{name} currently holds {used} active items; a quota of {quota} would put them over. Set {used} or higher, or ask them to clear items first.");
        }

        user.ItemQuota = quota;
        var updated = await users.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return OperationResult.Fail(FormatErrors(updated));
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> UpdateMemberAsync(
        string userId,
        string userName,
        string email,
        int quota,
        ClaimsPrincipal actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!UserClaims.IsAdmin(actor))
        {
            return OperationResult.Fail(AdministratorsOnly);
        }

        userName = userName?.Trim() ?? "";
        email = email?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(userName))
        {
            return OperationResult.Fail("Username is required.");
        }

        if (userName.Length > 64)
        {
            return OperationResult.Fail("Username is too long.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return OperationResult.Fail("Email is required.");
        }

        if (!new EmailAddressAttribute().IsValid(email))
        {
            return OperationResult.Fail("Enter a valid email address.");
        }

        if (quota < 0)
        {
            return OperationResult.Fail("Quota cannot be negative.");
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        var nameOwner = await users.FindByNameAsync(userName);
        if (nameOwner is not null && nameOwner.Id != user.Id)
        {
            return OperationResult.Fail("That username is already in use.");
        }

        var emailOwner = await users.FindByEmailAsync(email);
        if (emailOwner is not null && emailOwner.Id != user.Id)
        {
            return OperationResult.Fail("That email is already in use.");
        }

        await using var db = await factory.CreateDbContextAsync();
        var used = await CapacityQueries.UserUsageAsync(db, user.Id);
        if (quota < used)
        {
            var name = FoodDisplay.OwnerLabel(user);
            return OperationResult.Fail(
                $"{name} currently holds {used} active items; a quota of {quota} would put them over. Set {used} or higher, or ask them to clear items first.");
        }

        var normalizedName = users.NormalizeName(userName);
        if (!string.Equals(user.NormalizedUserName, normalizedName, StringComparison.Ordinal))
        {
            var renamed = await users.SetUserNameAsync(user, userName);
            if (!renamed.Succeeded)
            {
                return OperationResult.Fail(FormatErrors(renamed));
            }
        }

        var normalizedEmail = users.NormalizeEmail(email);
        if (!string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
        {
            var setEmail = await users.SetEmailAsync(user, email);
            if (!setEmail.Succeeded)
            {
                return OperationResult.Fail(FormatErrors(setEmail));
            }

            user.EmailConfirmed = true;
            var confirmed = await users.UpdateAsync(user);
            if (!confirmed.Succeeded)
            {
                return OperationResult.Fail(FormatErrors(confirmed));
            }
        }

        if (user.ItemQuota != quota)
        {
            user.ItemQuota = quota;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return OperationResult.Fail(FormatErrors(updated));
            }
        }

        return OperationResult.Ok();
    }

    public async Task<OperationResult> SetAdminAsync(string userId, bool isAdmin, ClaimsPrincipal actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!UserClaims.IsAdmin(actor))
        {
            return OperationResult.Fail(AdministratorsOnly);
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null)
        {
            return OperationResult.Fail("User not found.");
        }

        if (!isAdmin && user.Id == UserClaims.GetUserId(actor))
        {
            return OperationResult.Fail(CannotRemoveOwnAdmin);
        }

        var alreadyAdmin = await users.IsInRoleAsync(user, "Admin");
        if (alreadyAdmin == isAdmin)
        {
            return OperationResult.Ok();
        }

        var changed = isAdmin
            ? await users.AddToRoleAsync(user, "Admin")
            : await users.RemoveFromRoleAsync(user, "Admin");
        if (!changed.Succeeded)
        {
            return OperationResult.Fail(FormatErrors(changed));
        }

        var stamped = await users.UpdateSecurityStampAsync(user);
        if (!stamped.Succeeded)
        {
            return OperationResult.Fail(FormatErrors(stamped));
        }

        return OperationResult.Ok();
    }

    private static string FormatErrors(IdentityResult result)
        => string.Join(" ", result.Errors.Select(e => e.Description));
}
