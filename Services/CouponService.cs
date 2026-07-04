using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public class CouponService : ICouponService
{
    private const int OrdersPerLoyaltyCoupon = 10;
    private const decimal WelcomeDiscountPercent = 10m;
    private const decimal LoyaltyDiscountPercent = 50m;

    private readonly ApplicationDbContext _context;

    public CouponService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Coupon?> EnsureWelcomeCouponAsync(string userId)
    {
        var alreadyHasOne = await _context.Coupons.AnyAsync(c => c.UserId == userId && c.Source == CouponSource.Welcome);
        if (alreadyHasOne)
        {
            return null;
        }

        var coupon = new Coupon
        {
            Code = await GenerateUniqueCodeAsync("WELCOME"),
            UserId = userId,
            DiscountPercent = WelcomeDiscountPercent,
            Source = CouponSource.Welcome,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();
        return coupon;
    }

    public async Task<Coupon?> IssueLoyaltyCouponIfEligibleAsync(string userId)
    {
        var paidOrderCount = await _context.Orders.CountAsync(o => o.UserId == userId && o.IsPaid);
        if (paidOrderCount == 0 || paidOrderCount % OrdersPerLoyaltyCoupon != 0)
        {
            return null;
        }

        var coupon = new Coupon
        {
            Code = await GenerateUniqueCodeAsync("LOYALTY"),
            UserId = userId,
            DiscountPercent = LoyaltyDiscountPercent,
            Source = CouponSource.LoyaltyMilestone,
            ExpiresAt = DateTime.UtcNow.AddDays(60)
        };

        _context.Coupons.Add(coupon);
        await _context.SaveChangesAsync();
        return coupon;
    }

    public async Task<Coupon?> ValidateAsync(string code, string userId)
    {
        var normalized = code.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;
        return await _context.Coupons.FirstOrDefaultAsync(c =>
            c.Code == normalized &&
            c.UserId == userId &&
            !c.IsUsed &&
            (c.ExpiresAt == null || c.ExpiresAt >= now));
    }

    public async Task<List<Coupon>> GetUserCouponsAsync(string userId)
    {
        return await _context.Coupons
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task MarkUsedAsync(Coupon coupon, int orderId)
    {
        coupon.IsUsed = true;
        coupon.UsedAt = DateTime.UtcNow;
        coupon.OrderId = orderId;
        await _context.SaveChangesAsync();
    }

    private async Task<string> GenerateUniqueCodeAsync(string prefix)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            var code = $"{prefix}-{suffix}";
            if (!await _context.Coupons.AnyAsync(c => c.Code == code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique coupon code.");
    }
}
