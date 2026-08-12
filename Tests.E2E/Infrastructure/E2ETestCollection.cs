namespace WorldLinkMaster.E2E.Infrastructure;

/// <summary>
/// Shares one E2EWebAppFactory (one app boot, one "e2e_test" schema reset) and one
/// PlaywrightFixture (one browser process) across every test class in the E2E run — both are
/// expensive to set up and safe to reuse, since per-test isolation comes from unique test data
/// (GUID-suffixed emails, etc.) and a fresh IBrowserContext per test class, not from resetting
/// the whole app/schema between tests.
/// </summary>
[CollectionDefinition(Name)]
public class E2ETestCollection : ICollectionFixture<E2EWebAppFactory>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "E2E";
}
