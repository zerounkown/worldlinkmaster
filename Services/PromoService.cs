using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public class PromoService : IPromoService
{
    private readonly ApplicationDbContext _context;

    public PromoService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PromoEvent?> GetTopActiveEventAsync()
    {
        var now = DateTime.UtcNow.Date;
        return await _context.PromoEvents
            .Where(e => e.IsActive && e.StartDate <= now && e.EndDate >= now)
            .OrderByDescending(e => e.DiscountPercent)
            .FirstOrDefaultAsync();
    }

    public async Task<decimal> GetActiveDiscountPercentAsync()
    {
        var active = await GetTopActiveEventAsync();
        return active?.DiscountPercent ?? 0m;
    }

    public async Task<List<PromoEvent>> GetShowcaseEventsAsync(int take = 6)
    {
        var now = DateTime.UtcNow.Date;
        return await _context.PromoEvents
            .Where(e => e.IsActive && e.EndDate >= now)
            .OrderBy(e => e.StartDate)
            .Take(take)
            .ToListAsync();
    }

    public decimal ApplyDiscount(decimal basePrice, decimal discountPercent)
    {
        if (discountPercent <= 0)
        {
            return basePrice;
        }

        var discounted = basePrice * (1 - discountPercent / 100m);
        return Math.Round(discounted, 2);
    }
}
