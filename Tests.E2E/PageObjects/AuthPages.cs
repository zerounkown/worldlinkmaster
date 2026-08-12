using Microsoft.Playwright;

namespace WorldLinkMaster.E2E.PageObjects;

/// <summary>
/// Registration (Areas/Identity/Pages/Account/Register) and login (.../Login) — both standard
/// ASP.NET Identity Razor Pages. RequireConfirmedAccount=false in Development, so a freshly
/// registered account can sign in immediately with no email-confirmation step.
/// </summary>
public class AuthPages
{
    private readonly IPage _page;
    private readonly string _baseUrl;

    public AuthPages(IPage page, string baseUrl)
    {
        _page = page;
        _baseUrl = baseUrl;
    }

    public static (string Email, string Password) GenerateCredentials()
    {
        var email = $"e2e-{Guid.NewGuid():N}@example.com";
        const string password = "E2eTest!2026";
        return (email, password);
    }

    public async Task RegisterAsync(string email, string password)
    {
        await _page.GotoAsync($"{_baseUrl}Identity/Account/Register");
        await _page.FillAsync("#Input_Email", email);
        await _page.FillAsync("#Input_Password", password);
        await _page.FillAsync("#Input_ConfirmPassword", password);
        await _page.ClickAsync("#registerSubmit");
    }

    public async Task LoginAsync(string email, string password)
    {
        await _page.GotoAsync($"{_baseUrl}Identity/Account/Login");
        await _page.FillAsync("#Input_Email", email);
        await _page.FillAsync("#Input_Password", password);
        await _page.ClickAsync("#login-submit");
    }

    /// <summary>The logout form only renders in _LoginPartial when SignInManager.IsSignedIn is true.</summary>
    public ILocator LogoutForm => _page.Locator("form[action*='/Account/Logout']");
}
