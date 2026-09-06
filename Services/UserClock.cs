using System.Security.Claims;
using FridgeManager.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace FridgeManager.Services;

public sealed class UserClock(
    IDbContextFactory<AppDbContext> factory,
    IJSRuntime js,
    IOptions<AppOptions> options) : IAsyncDisposable
{
    internal const string ModulePath = "./js/time-zone.js";
    internal const string GetMethod = "get";

    private IJSObjectReference? _module;
    private bool _resolved;

    public TimeZoneInfo Zone { get; private set; } = TimeZoneInfo.Utc;

    public DateOnly Today { get; private set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task ResolveAsync(ClaimsPrincipal user)
    {
        if (_resolved)
        {
            return;
        }

        string? saved = null;
        var userId = UserClaims.GetUserId(user);
        if (!string.IsNullOrEmpty(userId))
        {
            await using var db = await factory.CreateDbContextAsync();
            saved = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.TimeZoneId)
                .FirstOrDefaultAsync();
        }

        string? browser = null;
        if (string.IsNullOrWhiteSpace(saved))
        {
            browser = await TryGetBrowserTimeZoneAsync();
        }

        Zone = Resolve(saved, browser, options.Value.DefaultTimeZone);
        Today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone));
        _resolved = true;
    }

    public DateTime ToLocal(DateTime utc)
        => TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utc), Zone);

    internal static TimeZoneInfo Resolve(string? saved, string? browser, string fallbackId)
    {
        if (TryFind(saved, out var zone))
        {
            return zone;
        }

        if (TryFind(browser, out zone))
        {
            return zone;
        }

        if (TryFind(fallbackId, out zone))
        {
            return zone;
        }

        return TimeZoneInfo.Utc;
    }

    private async Task<string?> TryGetBrowserTimeZoneAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            var id = await module.InvokeAsync<string?>(GetMethod);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch (JSException)
        {
            return null;
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    private static bool TryFind(string? id, out TimeZoneInfo zone)
    {
        if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var found))
        {
            zone = found;
            return true;
        }

        zone = TimeZoneInfo.Utc;
        return false;
    }

    private static DateTime AsUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
