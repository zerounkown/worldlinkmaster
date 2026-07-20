namespace WorldLinkMaster.Web.Services;

// Ambient, per-request currency preference — set once by middleware from the "currency"
// cookie, then read anywhere (views, extension methods) without threading it through every
// call site. AsyncLocal keeps it scoped to the current request even across await points.
public static class CurrencyContext
{
    private static readonly AsyncLocal<string?> _current = new();

    public static string Current
    {
        get => _current.Value ?? "AED";
        set => _current.Value = value;
    }
}
