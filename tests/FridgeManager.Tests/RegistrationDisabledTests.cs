namespace FridgeManager.Tests;

public sealed class RegistrationDisabledTests
{
    [Theory]
    [InlineData("Components/Account/Pages/Register.razor")]
    [InlineData("Components/Account/Pages/RegisterConfirmation.razor")]
    public void RegisterPage_OnlyRedirectsToLogin(string relativePath)
    {
        var source = RepoFile(relativePath);

        Assert.Contains("RedirectTo(\"Account/Login\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UserManager", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href=", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountPages_RemainExcludedFromInteractiveRouting()
    {
        var imports = RepoFile("Components/Account/Pages/_Imports.razor");
        Assert.Contains("[ExcludeFromInteractiveRouting]", imports, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAndNav_DoNotLinkToRegister()
    {
        var login = RepoFile("Components/Account/Pages/Login.razor");
        var nav = RepoFile("Components/Layout/NavMenu.razor");

        Assert.DoesNotContain("Account/Register", login, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Account/Register", nav, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RegisterConfirmation", login, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RegisterConfirmation", nav, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                return File.ReadAllText(full);
            }
        }

        throw new InvalidOperationException(
            $"Could not find '{relativePath}' above {AppContext.BaseDirectory}.");
    }
}
