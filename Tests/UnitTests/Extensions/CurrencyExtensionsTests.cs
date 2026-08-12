using WorldLinkMaster.Web.Extensions;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Extensions;

public class CurrencyExtensionsTests
{
    [Fact]
    public void ToAed_FormatsWithAedPrefixAndTwoDecimals()
    {
        var result = 1234.5m.ToAed();

        Assert.Equal("AED 1,234.50", result);
    }

    [Fact]
    public void ToDisplayCurrency_WhenAed_ReturnsAedFormat()
    {
        CurrencyContext.Current = "AED";

        var result = 100m.ToDisplayCurrency();

        Assert.Equal("AED 100.00", result);
    }

    [Fact]
    public void ToDisplayCurrency_WhenUsd_ConvertsAndFormatsWithDollarSign()
    {
        CurrencyContext.Current = "USD";

        var result = 367m.ToDisplayCurrency();

        Assert.Equal("$100.00", result);
    }

    [Fact]
    public void ToDisplayCurrencyValue_WhenAed_ReturnsSameAmount()
    {
        CurrencyContext.Current = "AED";

        var result = 100m.ToDisplayCurrencyValue();

        Assert.Equal(100m, result);
    }

    [Fact]
    public void ToDisplayCurrencyValue_WhenUsd_DividesByPeggedRate()
    {
        CurrencyContext.Current = "USD";

        var result = 367m.ToDisplayCurrencyValue();

        Assert.Equal(100m, result);
    }

    [Fact]
    public void FromDisplayCurrencyToAed_WhenAed_ReturnsSameAmount()
    {
        CurrencyContext.Current = "AED";

        var result = 100m.FromDisplayCurrencyToAed();

        Assert.Equal(100m, result);
    }

    [Fact]
    public void FromDisplayCurrencyToAed_WhenUsd_MultipliesByPeggedRate()
    {
        CurrencyContext.Current = "USD";

        var result = 100m.FromDisplayCurrencyToAed();

        Assert.Equal(367m, result);
    }

    [Fact]
    public void ToDisplayCurrency_AndFromDisplayCurrencyToAed_AreInverseOperations()
    {
        CurrencyContext.Current = "USD";
        var originalAed = 550m;

        var displayValue = originalAed.ToDisplayCurrencyValue();
        var roundTripped = displayValue.FromDisplayCurrencyToAed();

        // Dividing then multiplying by the pegged rate can leave a tiny decimal residue (decimal
        // division doesn't always invert exactly) — round to cents, which is the precision that
        // actually matters for a displayed price, rather than asserting exact decimal equality.
        Assert.Equal(originalAed, Math.Round(roundTripped, 2));
    }
}
