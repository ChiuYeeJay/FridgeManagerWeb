using FridgeManager.Services.Models;
using Microsoft.JSInterop;

namespace FridgeManager.Services;

public sealed class FoodSortPreference(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Pages/FoodList.razor.js";
    internal const string GetMethod = "get";
    internal const string SetMethod = "set";

    private IJSObjectReference? _module;

    public async Task<(FoodSort Sort, bool Descending)?> ReadAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            var raw = await module.InvokeAsync<string?>(GetMethod);
            return TryParse(raw);
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

    public async Task WriteAsync(FoodSort sort, bool descending)
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync(SetMethod, Format(sort, descending));
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }

    internal static string Format(FoodSort sort, bool descending)
        => $"{sort}:{(descending ? "desc" : "asc")}";

    internal static (FoodSort Sort, bool Descending)? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(':', 2);
        if (!Enum.TryParse<FoodSort>(parts[0], ignoreCase: true, out var sort))
        {
            return null;
        }

        var descending = parts.Length > 1
            && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
        return (sort, descending);
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);

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
