using System.Security.Claims;
using FridgeManager.Services.Models;

namespace FridgeManager.Services;

public interface IUserAdminService
{
    Task<OperationResult<List<AdminUserDto>>> GetUsersAsync(ClaimsPrincipal actor);
    Task<OperationResult> CreateUserAsync(string userName, string email, string password, int quota, ClaimsPrincipal actor);
    Task<OperationResult> SetActiveAsync(string userId, bool isActive, ClaimsPrincipal actor);
    Task<OperationResult> SetQuotaAsync(string userId, int quota, ClaimsPrincipal actor);
}
