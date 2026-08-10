using Microsoft.Playwright;
using WorldLinkMaster.E2E.Infrastructure;
using WorldLinkMaster.E2E.PageObjects;

namespace WorldLinkMaster.E2E.Journeys;

[Collection(E2ETestCollection.Name)]
public class AuthTests : E2ETestBase
{
    public AuthTests(E2EWebAppFactory app, PlaywrightFixture playwright) : base(app, playwright)
    {
    }

    [Fact]
    public async Task Register_NewAccount_SignsInImmediately()
    {
        // RequireConfirmedAccount is false in Development (Program.cs), so registration should
        // sign the user in without an email-confirmation step.
        var (email, password) = AuthPages.GenerateCredentials();
        var auth = new AuthPages(Page, BaseUrl);

        await auth.RegisterAsync(email, password);

        await Assertions.Expect(auth.LogoutForm).ToBeAttachedAsync();
    }

    [Fact]
    public async Task Login_WithExistingAccount_SignsIn()
    {
        var (email, password) = AuthPages.GenerateCredentials();
        var auth = new AuthPages(Page, BaseUrl);
        await auth.RegisterAsync(email, password);
        await Assertions.Expect(auth.LogoutForm).ToBeAttachedAsync();

        // Log out, then log back in as the same, already-existing account. The logout button
        // lives inside a Bootstrap dropdown-menu that starts collapsed — open it first.
        await Page.ClickAsync("button.account-toggle");
        await auth.LogoutForm.Locator("button").ClickAsync();
        await Page.WaitForURLAsync(url => !url.Contains("/Identity/Account/Login"));
        await Assertions.Expect(auth.LogoutForm).Not.ToBeAttachedAsync();

        await auth.LoginAsync(email, password);

        await Assertions.Expect(auth.LogoutForm).ToBeAttachedAsync();
    }
}
