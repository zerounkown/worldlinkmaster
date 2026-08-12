using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

// Per-request cached lookup of the signed-in shopper's favorited product IDs — the product
// card partial renders dozens of times per page (grid, carousels, "you may also like"), so
// every card asking "is this favorited?" must share one query instead of firing its own.
public class FavoriteService : IFavoriteService
{
    private const string CacheKey = "FavoriteProductIds";

    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public FavoriteService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<HashSet<int>> GetFavoriteProductIdsAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return new HashSet<int>();
        }

        if (httpContext.Items[CacheKey] is HashSet<int> cached)
        {
            return cached;
        }

        var userId = _userManager.GetUserId(httpContext.User);
        var ids = userId == null
            ? new HashSet<int>()
            : (await _context.Favorites.Where(f => f.UserId == userId).Select(f => f.ProductId).ToListAsync()).ToHashSet();

        httpContext.Items[CacheKey] = ids;
        return ids;
    }

    public async Task<bool> IsFavoriteAsync(int productId)
    {
        var ids = await GetFavoriteProductIdsAsync();
        return ids.Contains(productId);
    }

    public async Task<int> GetCountAsync()
    {
        var ids = await GetFavoriteProductIdsAsync();
        return ids.Count;
    }
}
