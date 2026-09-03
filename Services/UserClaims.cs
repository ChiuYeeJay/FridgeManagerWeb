using System.Security.Claims;
using FridgeManager.Data.Entities;

namespace FridgeManager.Services;

public static class UserClaims
{
    public static string? GetUserId(ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier);

    public static bool IsAdmin(ClaimsPrincipal user)
        => user.IsInRole("Admin");

    public static bool CanModify(ClaimsPrincipal user, FoodItem item)
        => IsAdmin(user) || GetUserId(user) == item.OwnerId;
}
