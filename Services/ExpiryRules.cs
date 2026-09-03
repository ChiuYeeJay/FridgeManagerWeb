using FridgeManager.Data.Enums;

namespace FridgeManager.Services;

public static class ExpiryRules
{
    public static ExpiryState Of(DateOnly expirationDate, DateOnly today)
    {
        if (expirationDate < today)
        {
            return ExpiryState.Expired;
        }

        if (expirationDate <= today.AddDays(3))
        {
            return ExpiryState.ExpiringSoon;
        }

        return ExpiryState.Normal;
    }
}
