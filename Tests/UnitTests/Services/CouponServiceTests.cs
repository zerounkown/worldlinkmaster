using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Tests.UnitTests.Services;

public class CouponServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task EnsureWelcomeCouponAsync_FirstTime_CreatesA10PercentCoupon()
    {
        using var context = CreateContext();
        var service = new CouponService(context);

        var coupon = await service.EnsureWelcomeCouponAsync("user-1");

        Assert.NotNull(coupon);
        Assert.Equal(10m, coupon!.DiscountPercent);
        Assert.Equal(CouponSource.Welcome, coupon.Source);
        Assert.Equal("user-1", coupon.UserId);
        Assert.StartsWith("WELCOME-", coupon.Code);
    }

    [Fact]
    public async Task EnsureWelcomeCouponAsync_WhenUserAlreadyHasOne_ReturnsNull()
    {
        using var context = CreateContext();
        var service = new CouponService(context);
        await service.EnsureWelcomeCouponAsync("user-1");

        var second = await service.EnsureWelcomeCouponAsync("user-1");

        Assert.Null(second);
        Assert.Equal(1, await context.Coupons.CountAsync(c => c.UserId == "user-1"));
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(15, false)]
    [InlineData(20, true)]
    public async Task IssueLoyaltyCouponIfEligibleAsync_OnlyFiresEveryTenthPaidOrder(int paidOrderCount, bool expectCoupon)
    {
        using var context = CreateContext();
        for (var i = 0; i < paidOrderCount; i++)
        {
            context.Orders.Add(new Order { UserId = "user-1", IsPaid = true, ShippingName = "x", ShippingAddress = "x", ShippingCity = "x", ShippingState = "x", ShippingZip = "x", ShippingPhone = "x" });
        }
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var coupon = await service.IssueLoyaltyCouponIfEligibleAsync("user-1");

        if (expectCoupon)
        {
            Assert.NotNull(coupon);
            Assert.Equal(50m, coupon!.DiscountPercent);
            Assert.Equal(CouponSource.LoyaltyMilestone, coupon.Source);
        }
        else
        {
            Assert.Null(coupon);
        }
    }

    [Fact]
    public async Task ValidateAsync_UnknownCode_ReturnsNull()
    {
        using var context = CreateContext();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("NOPE-123456", "user-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PersonalCoupon_BelongingToDifferentUser_ReturnsNull()
    {
        using var context = CreateContext();
        context.Coupons.Add(new Coupon { Code = "WELCOME-ABC123", UserId = "owner-user", DiscountPercent = 10, Source = CouponSource.Welcome, IsActive = true });
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("WELCOME-ABC123", "someone-else");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PersonalCoupon_AlreadyUsed_ReturnsNull()
    {
        using var context = CreateContext();
        context.Coupons.Add(new Coupon { Code = "WELCOME-ABC123", UserId = "user-1", DiscountPercent = 10, Source = CouponSource.Welcome, IsActive = true, IsUsed = true });
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("WELCOME-ABC123", "user-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PersonalCoupon_OwnedAndUnused_ReturnsCoupon()
    {
        using var context = CreateContext();
        context.Coupons.Add(new Coupon { Code = "WELCOME-ABC123", UserId = "user-1", DiscountPercent = 10, Source = CouponSource.Welcome, IsActive = true, IsUsed = false });
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("welcome-abc123", "user-1");

        Assert.NotNull(result);
        Assert.Equal("WELCOME-ABC123", result!.Code);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredCoupon_ReturnsNull()
    {
        using var context = CreateContext();
        context.Coupons.Add(new Coupon
        {
            Code = "OLD-CODE01",
            UserId = "user-1",
            DiscountPercent = 10,
            Source = CouponSource.Welcome,
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("OLD-CODE01", "user-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PublicCoupon_AtMaxRedemptions_ReturnsNull()
    {
        using var context = CreateContext();
        context.Coupons.Add(new Coupon
        {
            Code = "PROMO-FULL01",
            UserId = null,
            DiscountPercent = 20,
            Source = CouponSource.AdminPromo,
            IsActive = true,
            MaxRedemptions = 5,
            RedemptionCount = 5
        });
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("PROMO-FULL01", "any-user");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PublicCoupon_AlreadyRedeemedByThisUser_ReturnsNull()
    {
        using var context = CreateContext();
        var coupon = new Coupon { Code = "PROMO-ONCE01", UserId = null, DiscountPercent = 20, Source = CouponSource.AdminPromo, IsActive = true };
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();
        context.CouponRedemptions.Add(new CouponRedemption { CouponId = coupon.Id, UserId = "user-1", OrderId = 1 });
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("PROMO-ONCE01", "user-1");

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateAsync_PublicCoupon_NeverRedeemedByThisUser_ReturnsCoupon()
    {
        using var context = CreateContext();
        context.Coupons.Add(new Coupon { Code = "PROMO-FRESH01", UserId = null, DiscountPercent = 20, Source = CouponSource.AdminPromo, IsActive = true });
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        var result = await service.ValidateAsync("PROMO-FRESH01", "user-1");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task MarkUsedAsync_PersonalCoupon_SetsUsedFlagAndOrderId()
    {
        using var context = CreateContext();
        var coupon = new Coupon { Code = "WELCOME-XYZ999", UserId = "user-1", DiscountPercent = 10, Source = CouponSource.Welcome };
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        await service.MarkUsedAsync(coupon, orderId: 42, userId: "user-1");

        var reloaded = await context.Coupons.FindAsync(coupon.Id);
        Assert.True(reloaded!.IsUsed);
        Assert.Equal(42, reloaded.OrderId);
        Assert.NotNull(reloaded.UsedAt);
    }

    [Fact]
    public async Task MarkUsedAsync_PublicCoupon_IncrementsRedemptionCountAndRecordsRedemption()
    {
        using var context = CreateContext();
        var coupon = new Coupon { Code = "PROMO-COUNT01", UserId = null, DiscountPercent = 20, Source = CouponSource.AdminPromo, RedemptionCount = 3 };
        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();
        var service = new CouponService(context);

        await service.MarkUsedAsync(coupon, orderId: 99, userId: "user-1");

        var reloaded = await context.Coupons.FindAsync(coupon.Id);
        Assert.Equal(4, reloaded!.RedemptionCount);
        Assert.Single(await context.CouponRedemptions.Where(r => r.CouponId == coupon.Id && r.UserId == "user-1").ToListAsync());
    }
}
