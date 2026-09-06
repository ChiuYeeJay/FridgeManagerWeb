using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using FridgeManager.Components;
using FridgeManager.Components.Account;
using FridgeManager.Data;
using FridgeManager.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
        options.DetailedErrors = builder.Environment.IsDevelopment());

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? NpgsqlConnectionStrings.FromDatabaseUrl(builder.Configuration["DATABASE_URL"])
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<AppDbContext>(opt => opt.UseNpgsql(connectionString));
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("FridgeManager");

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p.RequireRole("Admin"));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});
builder.Services.AddHealthChecks();
builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.SectionName));
AddImageStorage(builder);

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<ICapacityService, CapacityService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<FoodListState>();
builder.Services.AddScoped<FoodSortPreference>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using (var db = await factory.CreateDbContextAsync())
    {
        await db.Database.MigrateAsync();
    }

    await StartupBootstrap.RunAsync(scope.ServiceProvider);

    if (app.Environment.IsDevelopment())
    {
        await DbSeeder.SeedDemoDataAsync(scope.ServiceProvider);
    }
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads"))
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Response.Headers.XContentTypeOptions = "nosniff";
    }

    await next();
});

app.UseAntiforgery();

var uploadsPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

static void AddImageStorage(WebApplicationBuilder builder)
{
    var provider = builder.Configuration[$"{ImageStorageOptions.SectionName}:Provider"] ?? "";
    switch (provider)
    {
        case "Local":
            builder.Services.AddSingleton<IImageStorage, LocalImageStorage>();
            break;
        case "R2":
            builder.Services.AddOptions<R2Options>()
                .Bind(builder.Configuration.GetSection(R2Options.SectionName))
                .Validate(o => o.IsComplete, "R2 storage requires ServiceUrl, AccessKeyId, SecretAccessKey, BucketName, and PublicBaseUrl.")
                .ValidateOnStart();
            builder.Services.AddSingleton<IAmazonS3>(sp =>
            {
                var r2 = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<R2Options>>().Value;
                var credentials = new BasicAWSCredentials(r2.AccessKeyId, r2.SecretAccessKey);
                var config = new AmazonS3Config
                {
                    ServiceURL = r2.ServiceUrl,
                    ForcePathStyle = true,
                    AuthenticationRegion = "auto",
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
                };
                return new AmazonS3Client(credentials, config);
            });
            builder.Services.AddSingleton<IImageStorage, R2ImageStorage>();
            break;
        default:
            throw new InvalidOperationException(
                $"ImageStorage:Provider '{provider}' is not supported. Use Local or R2.");
    }
}

