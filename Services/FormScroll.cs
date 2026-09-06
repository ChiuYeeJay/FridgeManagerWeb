using Microsoft.JSInterop;

namespace FridgeManager.Services;

public sealed class FormScroll(IJSRuntime js) : IAsyncDisposable
{
    internal const string ModulePath = "./Components/Pages/FoodForm.razor.js";
    internal const string TopMethod = "scrollToTop";
    internal const string InvalidMethod = "scrollToInvalid";

    private IJSObjectReference? _module;

    public async Task ToTopAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync(TopMethod);
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }

    public async Task ToInvalidAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            await module.InvokeVoidAsync(InvalidMethod);
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
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
