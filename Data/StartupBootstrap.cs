using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FridgeManager.Data;

public static class StartupBootstrap
{
    public static async Task RunAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        var env = services.GetRequiredService<IHostEnvironment>();
        var options = services.GetRequiredService<IOptions<SeedOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(StartupBootstrap));
        var factory = services.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await DbSeeder.EnsureRolesAsync(roles);

        var admins = await users.GetUsersInRoleAsync("Admin");
        if (admins.Count == 0)
        {
            await CreateBootstrapAdminAsync(users, env, options, logger);
            admins = await users.GetUsersInRoleAsync("Admin");
        }

        if (options.DemoData)
        {
            await using var db = await factory.CreateDbContextAsync();
            var hasItems = await db.FoodItems.AnyAsync();
            if (!hasItems)
            {
                await DbSeeder.SeedDemoDataAsync(services);
            }
        }
    }

    private static async Task CreateBootstrapAdminAsync(
        UserManager<ApplicationUser> users,
        IHostEnvironment env,
        SeedOptions options,
        ILogger logger)
    {
        var userName = options.AdminUserName?.Trim();
        var email = options.AdminEmail?.Trim();
        var password = options.AdminPassword;

        if (string.IsNullOrWhiteSpace(userName)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password))
        {
            if (env.IsProduction())
            {
                throw new InvalidOperationException(
                    "Production bootstrap requires Seed:AdminUserName, Seed:AdminEmail, and Seed:AdminPassword because no Admin user exists.");
            }

            logger.LogInformation(
                "No Admin user exists and Seed:Admin* is incomplete; skipping admin bootstrap.");
            return;
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = email,
            EmailConfirmed = true,
            IsActive = true
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create bootstrap admin: {FormatErrors(created)}");
        }

        var added = await users.AddToRoleAsync(user, "Admin");
        if (!added.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to add bootstrap admin to the Admin role: {FormatErrors(added)}");
        }

        logger.LogInformation("Created bootstrap Admin user '{UserName}'.", userName);
    }

    private static string FormatErrors(IdentityResult result)
        => string.Join("; ", result.Errors.Select(e => e.Description));
}
