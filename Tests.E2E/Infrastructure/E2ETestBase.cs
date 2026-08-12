using Microsoft.Playwright;

namespace WorldLinkMaster.E2E.Infrastructure;

/// <summary>
/// One IBrowserContext (and one Page) per test class, isolated cookies/storage from every other
/// test class in the run. Defaults every context to English ("en") via the app's own
/// ".AspNetCore.Culture" cookie format (Program.cs defaults a cookie-less visitor to Arabic/RTL,
/// which would make locators that key off English text nondeterministic) — LocalizationRtlTests
/// explicitly overrides this by switching language through the real UI.
/// </summary>
public abstract class E2ETestBase : IAsyncLifetime
{
    protected readonly E2EWebAppFactory App;
    protected readonly IBrowser Browser;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;
    protected string BaseUrl => App.BaseUrl;

    protected E2ETestBase(E2EWebAppFactory app, PlaywrightFixture playwright)
    {
        App = app;
        Browser = playwright.Browser;
    }

    /// <summary>Override to test a different viewport (e.g. mobile) — see MobileViewportTests.</summary>
    protected virtual ViewportSize ViewportSize => new() { Width = 1280, Height = 900 };

    public virtual async Task InitializeAsync()
    {
        Context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = ViewportSize,
        });

        var baseUri = new Uri(BaseUrl);
        await Context.AddCookiesAsync(new[]
        {
            new Cookie
            {
                Name = ".AspNetCore.Culture",
                Value = "c=en|uic=en",
                Domain = baseUri.Host,
                Path = "/",
            },
        });

        Page = await Context.NewPageAsync();
    }

    public virtual async Task DisposeAsync()
    {
        await Context.CloseAsync();
    }

    protected string Url(string path) => new Uri(new Uri(BaseUrl), path).ToString();

    /// <summary>
    /// Navigates to the product listing and opens the first in-stock product's detail page —
    /// used by journeys that need "some real product" without depending on specific seeded
    /// names/slugs, which would make tests brittle against SeedData catalog changes.
    /// </summary>
    protected async Task OpenFirstProductDetailAsync()
    {
        await Page.GotoAsync(Url("Products"));
        var firstCard = Page.Locator("a.product-card-tilt").First;
        await firstCard.WaitForAsync();
        await firstCard.ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("/Products/Details"));
    }
}
