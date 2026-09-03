namespace FridgeManager.Services.Models;

public sealed record UserUsageDto(string UserId, string Name, int Used, int Quota, bool IsActive);
