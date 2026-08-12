using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Services;

// Per-request cached lookup of the signed-in shopper's compare-list product IDs — mirrors
// FavoriteService exactly, for the same reason (product cards render dozens of times per page
// and each one asking "is this in compare?" must share one query).
public class CompareService : ICompareService
{
    private const string CacheKey = "CompareProductIds";

    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CompareService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public async Task<HashSet<int>> GetCompareProductIdsAsync()
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
            : (await _context.CompareItems.Where(c => c.UserId == userId).Select(c => c.ProductId).ToListAsync()).ToHashSet();

        httpContext.Items[CacheKey] = ids;
        return ids;
    }

    public async Task<bool> IsInCompareAsync(int productId)
    {
        var ids = await GetCompareProductIdsAsync();
        return ids.Contains(productId);
    }

    public async Task<int> GetCountAsync()
    {
        var ids = await GetCompareProductIdsAsync();
        return ids.Count;
    }
}
