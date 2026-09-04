using FridgeManager.Data;
using FridgeManager.Data.Entities;
using FridgeManager.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FridgeManager.Tests;

public sealed class UserAdminServiceTests
{
    [Fact]
    public async Task SetQuotaAsync_BelowCurrentUsage_FailsAndNamesCount()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.SetQuotaAsync(host.Seed.AliceId, quota: 1, host.AdminPrincipal);

        Assert.False(result.Success);
        Assert.Contains("2 active items", result.Error);
        Assert.Contains("quota of 1", result.Error);

        await using var db = await host.Factory.CreateDbContextAsync();
        var alice = await db.Users.SingleAsync(u => u.Id == host.Seed.AliceId);
        Assert.Equal(2, alice.ItemQuota);
    }

    [Fact]
    public async Task SetQuotaAsync_EqualToCurrentUsage_Succeeds()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.SetQuotaAsync(host.Seed.AliceId, quota: 2, host.AdminPrincipal);

        Assert.True(result.Success);

        await using var db = await host.Factory.CreateDbContextAsync();
        var alice = await db.Users.SingleAsync(u => u.Id == host.Seed.AliceId);
        Assert.Equal(2, alice.ItemQuota);
    }

    [Fact]
    public async Task GetUsersAsync_ByNonAdmin_Fails()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.GetUsersAsync(Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Equal(UserAdminService.AdministratorsOnly, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SetQuotaAsync_ByNonAdmin_Fails()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.SetQuotaAsync(host.Seed.BobId, quota: 9, Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Equal(UserAdminService.AdministratorsOnly, result.Error);
    }

    [Fact]
    public async Task CreateUserAsync_ByAdmin_PersistsActiveUserWithRoleAndQuota()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.CreateUserAsync(
            "lena",
            "lena@fridge.local",
            "Passw0rd!",
            quota: 5,
            host.AdminPrincipal);

        Assert.True(result.Success);

        var created = await host.Users.FindByNameAsync("lena");
        Assert.NotNull(created);
        Assert.True(created.IsActive);
        Assert.True(created.EmailConfirmed);
        Assert.Equal("lena@fridge.local", created.Email);
        Assert.Equal(5, created.ItemQuota);
        Assert.True(await host.Users.IsInRoleAsync(created, "User"));
    }

    [Fact]
    public async Task CreateUserAsync_ByNonAdmin_Fails()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.CreateUserAsync(
            "intruder",
            "intruder@fridge.local",
            "Passw0rd!",
            quota: 5,
            Principals.For(host.Seed.AliceId));

        Assert.False(result.Success);
        Assert.Equal(UserAdminService.AdministratorsOnly, result.Error);
        Assert.Null(await host.Users.FindByNameAsync("intruder"));
    }

    [Fact]
    public async Task SetActiveAsync_False_DisablesUser()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.SetActiveAsync(host.Seed.AliceId, isActive: false, host.AdminPrincipal);

        Assert.True(result.Success);

        var alice = await host.Users.FindByIdAsync(host.Seed.AliceId);
        Assert.NotNull(alice);
        Assert.False(alice.IsActive);
    }

    [Fact]
    public async Task SetActiveAsync_DisablingSelf_Fails()
    {
        using var host = new ServiceHost();

        var result = await host.Admin.SetActiveAsync(host.Seed.AdminId, isActive: false, host.AdminPrincipal);

        Assert.False(result.Success);
        Assert.Contains("your own account", result.Error, StringComparison.OrdinalIgnoreCase);

        var admin = await host.Users.FindByIdAsync(host.Seed.AdminId);
        Assert.NotNull(admin);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task GetUsersAsync_ByAdmin_IncludesUsageAndDisabled()
    {
        using var host = new ServiceHost();
        await host.Admin.SetActiveAsync(host.Seed.BobId, isActive: false, host.AdminPrincipal);

        var result = await host.Admin.GetUsersAsync(host.AdminPrincipal);

        Assert.True(result.Success);
        Assert.NotNull(result.Value);
        var alice = result.Value.Single(u => u.UserId == host.Seed.AliceId);
        Assert.Equal(2, alice.ActiveCount);
        Assert.Equal(2, alice.Quota);
        Assert.True(alice.AtLimit);
        Assert.False(alice.IsAdmin);

        var admin = result.Value.Single(u => u.UserId == host.Seed.AdminId);
        Assert.True(admin.IsAdmin);

        var bob = result.Value.Single(u => u.UserId == host.Seed.BobId);
        Assert.False(bob.IsActive);
    }

    private sealed class ServiceHost : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public SqliteDbFactory Factory { get; } = new();
        public SeedData Seed { get; }
        public UserAdminService Admin { get; }
        public UserManager<ApplicationUser> Users { get; }
        public System.Security.Claims.ClaimsPrincipal AdminPrincipal { get; }

        public ServiceHost()
        {
            Seed = TestData.Seed(Factory);
            AdminPrincipal = Principals.For(Seed.AdminId, admin: true);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton<IDbContextFactory<AppDbContext>>(Factory);
            services.AddScoped<AppDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>();

            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
            Users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            EnsureRole(roles, "Admin");
            EnsureRole(roles, "User");
            var admin = Users.FindByIdAsync(Seed.AdminId).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Seeded admin missing.");
            Users.AddToRoleAsync(admin, "Admin").GetAwaiter().GetResult();

            Admin = new UserAdminService(Factory, Users);
        }

        private static void EnsureRole(RoleManager<IdentityRole> roles, string name)
        {
            if (!roles.RoleExistsAsync(name).GetAwaiter().GetResult())
            {
                var created = roles.CreateAsync(new IdentityRole(name)).GetAwaiter().GetResult();
                if (!created.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));
                }
            }
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
            Factory.Dispose();
        }
    }
}
