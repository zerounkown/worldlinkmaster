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

        var coupon = await _context.Coupons.FirstOrDefaultAsync(c => c.Code == normalized);
        if (coupon == null || !coupon.IsActive)
        {
            return null;
        }

        // Validity window applies to every coupon type.
        if (coupon.StartsAt.HasValue && coupon.StartsAt.Value > now)
        {
            return null;
        }
        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt.Value < now)
        {
            return null;
        }

        if (coupon.UserId != null)
        {
            // Personal single-use coupon: must belong to this user and be unused.
            return (coupon.UserId == userId && !coupon.IsUsed) ? coupon : null;
        }

        // Public (AdminPromo) coupon: enforce total cap and one redemption per customer.
        if (coupon.MaxRedemptions.HasValue && coupon.RedemptionCount >= coupon.MaxRedemptions.Value)
        {
            return null;
        }

        var alreadyRedeemed = await _context.CouponRedemptions
            .AnyAsync(r => r.CouponId == coupon.Id && r.UserId == userId);

        return alreadyRedeemed ? null : coupon;
    }

    public async Task<List<Coupon>> GetUserCouponsAsync(string userId)
    {
        return await _context.Coupons
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Coupon>> GetActivePublicCouponsAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var redeemedCouponIds = await _context.CouponRedemptions
            .Where(r => r.UserId == userId)
            .Select(r => r.CouponId)
            .ToListAsync();

        return await _context.Coupons
            .Where(c => c.UserId == null
                && c.Source == CouponSource.AdminPromo
                && c.IsActive
                && (c.StartsAt == null || c.StartsAt <= now)
                && (c.ExpiresAt == null || c.ExpiresAt >= now)
                && (c.MaxRedemptions == null || c.RedemptionCount < c.MaxRedemptions)
                && !redeemedCouponIds.Contains(c.Id))
            .OrderByDescending(c => c.DiscountPercent)
            .ToListAsync();
    }

    public async Task MarkUsedAsync(Coupon coupon, int orderId, string userId)
    {
        if (coupon.UserId != null)
        {
            // Personal single-use coupon.
            coupon.IsUsed = true;
            coupon.UsedAt = DateTime.UtcNow;
            coupon.OrderId = orderId;
        }
        else
        {
            // Public coupon: count the redemption and record who used it (once per customer).
            coupon.RedemptionCount += 1;
            _context.CouponRedemptions.Add(new CouponRedemption
            {
                CouponId = coupon.Id,
                UserId = userId,
                OrderId = orderId,
                RedeemedAt = DateTime.UtcNow
            });
        }

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
