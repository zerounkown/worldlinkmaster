using Microsoft.Playwright;

namespace WorldLinkMaster.E2E.Infrastructure;

/// <summary>
/// One shared Chromium instance for the whole E2E run (launching a browser per test would be
/// far slower for no isolation benefit — each test still gets its own IBrowserContext/page, which
/// is where cookie/storage isolation actually matters). Headless by default; set the
/// HEADED=1 environment variable for local visual debugging.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("HEADED") != "1",
        });
    }

    public async Task DisposeAsync()
    {
        await Browser.CloseAsync();
        _playwright?.Dispose();
    }
}
