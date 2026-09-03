using Microsoft.AspNetCore.Identity;

namespace FridgeManager.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public int ItemQuota { get; set; } = 5;
    public bool IsActive { get; set; } = true;
}
