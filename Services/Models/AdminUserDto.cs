namespace FridgeManager.Services.Models;

public sealed record AdminUserDto(
    string UserId,
    string UserName,
    string Email,
    int Quota,
    int ActiveCount,
    bool IsActive,
    bool IsAdmin)
{
    public bool AtLimit => ActiveCount >= Quota;

    public string DisplayName
    {
        get
        {
            var at = UserName.IndexOf('@');
            return at > 0 ? UserName[..at] : UserName;
        }
    }
}
