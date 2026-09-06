using FridgeManager.Services.Models;

namespace FridgeManager.Services;

public interface ICapacityService
{
    Task<int> GetShelfUsageAsync(int shelfId);
    Task<int> GetShelfRemainingAsync(int shelfId);
    Task<int> GetUserUsageAsync(string userId);
    Task<List<ShelfUsageDto>> GetAllShelfUsageAsync();
    Task<List<UserUsageDto>> GetAllUserUsageAsync();
    Task<DashboardStats> GetDashboardStatsAsync(DateOnly today);
}
