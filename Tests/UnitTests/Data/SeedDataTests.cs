using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.Tests.UnitTests.Data;

public class SeedDataTests
{
    [Fact]
    public void IsProductionDatabase_ProductionHost_ReturnsTrue()
    {
        var result = SeedData.IsProductionDatabase(
            "Host=aws-1-ap-south-1.pooler.supabase.com;Port=5432;Username=postgres;Password=x;Database=postgres");

        Assert.True(result);
    }

    [Fact]
    public void IsProductionDatabase_ProductionHost_IsCaseInsensitive()
    {
        var result = SeedData.IsProductionDatabase(
            "Host=AWS-1-AP-SOUTH-1.POOLER.SUPABASE.COM;Username=postgres;Password=x;Database=postgres");

        Assert.True(result);
    }

    [Fact]
    public void IsProductionDatabase_LocalHost_ReturnsFalse()
    {
        var result = SeedData.IsProductionDatabase(
            "Host=localhost;Port=5432;Username=postgres;Password=x;Database=worldlinkmaster_dev");

        Assert.False(result);
    }

    [Fact]
    public void IsProductionDatabase_UnrelatedRemoteHost_ReturnsFalse()
    {
        var result = SeedData.IsProductionDatabase(
            "Host=some-other-project.supabase.co;Username=postgres;Password=x;Database=postgres");

        Assert.False(result);
    }

    [Fact]
    public void IsProductionDatabase_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(SeedData.IsProductionDatabase(null));
        Assert.False(SeedData.IsProductionDatabase(""));
        Assert.False(SeedData.IsProductionDatabase("   "));
    }

    [Fact]
    public void IsProductionDatabase_UnparseableConnectionString_FailsSafeAsProduction()
    {
        var result = SeedData.IsProductionDatabase("not a real connection string ===");

        Assert.True(result);
    }
}
