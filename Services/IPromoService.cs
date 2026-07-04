using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

public interface IPromoService
{
    Task<PromoEvent?> GetTopActiveEventAsync();

    Task<decimal> GetActiveDiscountPercentAsync();

    Task<List<PromoEvent>> GetShowcaseEventsAsync(int take = 6);

    decimal ApplyDiscount(decimal basePrice, decimal discountPercent);
}
