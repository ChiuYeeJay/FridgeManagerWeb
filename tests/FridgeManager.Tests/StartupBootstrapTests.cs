using FridgeManager.Data;
using FridgeManager.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FridgeManager.Tests;

public sealed class StartupBootstrapTests
{
    private const string BootstrapPassword = "Passw0rd!";

    [Fact]
    public async Task RunAsync_CreatesAdmin_WhenNoneExists()
    {
        using var host = BootstrapHost.Empty(options =>
        {
            options.AdminUserName = "bootstrap-admin";
            options.AdminEmail = "bootstrap@example.com";
            options.AdminPassword = BootstrapPassword;
        });

        await StartupBootstrap.RunAsync(host.Services);

        var created = await host.Users.FindByEmailAsync("bootstrap@example.com");
        Assert.NotNull(created);
        Assert.Equal("bootstrap-admin", created.UserName);
        Assert.True(created.EmailConfirmed);
        Assert.True(created.IsActive);
        Assert.Equal(5, created.ItemQuota);
        Assert.True(await host.Users.IsInRoleAsync(created, "Admin"));
        Assert.True(await host.Users.CheckPasswordAsync(created, BootstrapPassword));
    }

    [Fact]
    public async Task RunAsync_DoesNotOverwriteExistingAdmin()
    {
        using var host = BootstrapHost.Empty(options =>
        {
            options.AdminUserName = "replacement";
            options.AdminEmail = "replacement@example.com";
            options.AdminPassword = "Other!Pass1";
        });

        var existing = new ApplicationUser
        {
            UserName = "existing-admin",
            Email = "existing@fridge.local",
            EmailConfirmed = true,
            IsActive = true,
            ItemQuota = 9
        };
        var created = await host.Users.CreateAsync(existing, BootstrapPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await host.Roles.CreateAsync(new IdentityRole("Admin"));
        await host.Users.AddToRoleAsync(existing, "Admin");

        await StartupBootstrap.RunAsync(host.Services);

        Assert.Null(await host.Users.FindByEmailAsync("replacement@example.com"));
        var stillThere = await host.Users.FindByEmailAsync("existing@fridge.local");
        Assert.NotNull(stillThere);
        Assert.Equal("existing-admin", stillThere.UserName);
        Assert.Equal(9, stillThere.ItemQuota);
        Assert.True(await host.Users.CheckPasswordAsync(stillThere, BootstrapPassword));
        Assert.False(await host.Users.CheckPasswordAsync(stillThere, "Other!Pass1"));
    }

    [Fact]
    public async Task RunAsync_Production_ThrowsWhenAdminValuesMissingAndNoAdmin()
    {
        using var host = BootstrapHost.Empty(_ => { }, Environments.Production);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => StartupBootstrap.RunAsync(host.Services));

        Assert.Contains("Seed:AdminUserName", ex.Message);
        Assert.Contains("Seed:AdminPassword", ex.Message);
        Assert.Contains("no Admin user exists", ex.Message);
        Assert.Empty(host.Users.Users);
    }

    [Fact]
    public async Task RunAsync_Production_DoesNotThrowWhenAdminAlreadyExistsAndValuesMissing()
    {
        using var host = BootstrapHost.Empty(_ => { }, Environments.Production);
        var existing = new ApplicationUser
        {
            UserName = "existing-admin",
            Email = "existing@fridge.local",
            EmailConfirmed = true,
            IsActive = true
        };
        Assert.True((await host.Users.CreateAsync(existing, BootstrapPassword)).Succeeded);
        await host.Roles.CreateAsync(new IdentityRole("Admin"));
        await host.Users.AddToRoleAsync(existing, "Admin");

        await StartupBootstrap.RunAsync(host.Services);

        Assert.Single(host.Users.Users);
    }

    [Fact]
    public async Task RunAsync_Development_SkipsAdminWhenValuesMissing()
    {
        using var host = BootstrapHost.Empty(_ => { });

        await StartupBootstrap.RunAsync(host.Services);

        Assert.Empty(host.Users.Users);
    }

    [Fact]
    public async Task RunAsync_DemoData_IsNoOpWhenItemsAlreadyExist()
    {
        using var host = BootstrapHost.WithFoodItems(options =>
        {
            options.AdminUserName = "bootstrap-admin";
            options.AdminEmail = "bootstrap@example.com";
            options.AdminPassword = BootstrapPassword;
            options.DemoData = true;
        });

        var existingAdmin = await host.Users.FindByIdAsync(TestData.AdminId);
        Assert.NotNull(existingAdmin);
        await host.Roles.CreateAsync(new IdentityRole("Admin"));
        await host.Users.AddToRoleAsync(existingAdmin, "Admin");

        var countBefore = await CountItemsAsync(host);
        Assert.Equal(4, countBefore);

        await StartupBootstrap.RunAsync(host.Services);

        Assert.Equal(4, await CountItemsAsync(host));
        Assert.Null(await host.Users.FindByEmailAsync("carol@fridge.local"));
        Assert.Null(await host.Users.FindByEmailAsync("bootstrap@example.com"));
    }

    [Fact]
    public async Task RunAsync_DemoData_LoadsWhenNoItemsExist()
    {
        using var host = BootstrapHost.Empty(options =>
        {
            options.AdminUserName = "bootstrap-admin";
            options.AdminEmail = "bootstrap@example.com";
            options.AdminPassword = BootstrapPassword;
            options.DemoData = true;
        });

        await StartupBootstrap.RunAsync(host.Services);

        await using var db = await host.Factory.CreateDbContextAsync();
        var itemCount = await db.FoodItems.CountAsync();
        Assert.InRange(itemCount, 25, 30);
        Assert.Equal(4, await db.Shelves.CountAsync());
        Assert.NotNull(await host.Users.FindByEmailAsync("alice@fridge.local"));
        Assert.NotNull(await host.Users.FindByEmailAsync("bootstrap@example.com"));
    }

    private static async Task<int> CountItemsAsync(BootstrapHost host)
    {
        await using var db = await host.Factory.CreateDbContextAsync();
        return await db.FoodItems.CountAsync();
    }

    private sealed class BootstrapHost : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public SqliteDbFactory Factory { get; }
        public IServiceProvider Services => _scope.ServiceProvider;
        public UserManager<ApplicationUser> Users { get; }
        public RoleManager<IdentityRole> Roles { get; }

        private BootstrapHost(SqliteDbFactory factory, Action<SeedOptions> configure, string environmentName)
        {
            Factory = factory;

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddSingleton<IDbContextFactory<AppDbContext>>(Factory);
            services.AddScoped<AppDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
            services.AddIdentityCore<ApplicationUser>()
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>();
            services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment
            {
                EnvironmentName = environmentName
            });
            services.Configure<SeedOptions>(configure);

            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateScope();
            Users = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        }

        public static BootstrapHost Empty(Action<SeedOptions> configure, string environmentName = "Development")
            => new(new SqliteDbFactory(), configure, environmentName);

        public static BootstrapHost WithFoodItems(Action<SeedOptions> configure)
        {
            var factory = new SqliteDbFactory();
            TestData.Seed(factory);
            return new BootstrapHost(factory, configure, Environments.Development);
        }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
            Factory.Dispose();
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public required string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "FridgeManager";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
