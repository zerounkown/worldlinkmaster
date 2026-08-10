using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Services;

// Mirrors FavoriteService/CompareService in shape, but adds one extra responsibility:
// UpsertReviewAsync also recalculates Product.Rating/ReviewCount from real ProductReviews rows
// so every other place those two fields feed (PLP star filter/facets, Compare table, homepage
// Top Rated/Best Sellers ordering, nav Specials sort) reflects genuine review data instead of
// the seeded demo numbers, per the site-wide decision made when this feature was scoped.
public class ProductReviewService : IProductReviewService
{
    private readonly ApplicationDbContext _context;

    public ProductReviewService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ProductReviewDisplay>> GetReviewsAsync(int productId)
    {
        return await _context.ProductReviews
            .Where(r => r.ProductId == productId)
            .Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ProductReviewDisplay
            {
                Rating = r.Rating,
                Comment = r.Comment,
                CreatedAt = r.CreatedAt,
                ReviewerName = ReviewerName(r.User)
            })
            .ToListAsync();
    }

    public async Task<ProductReview?> GetUserReviewAsync(int productId, string userId)
    {
        return await _context.ProductReviews
            .FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);
    }

    public async Task<(ReviewSubmitResult result, bool isUpdate)> UpsertReviewAsync(int productId, string userId, int rating, string comment)
    {
        if (rating < 1 || rating > 5)
        {
            return (ReviewSubmitResult.InvalidRating, false);
        }

        comment = comment?.Trim() ?? string.Empty;
        if (comment.Length < 10 || comment.Length > 1000)
        {
            return (ReviewSubmitResult.InvalidCommentLength, false);
        }

        var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product == null)
        {
            return (ReviewSubmitResult.ProductNotFound, false);
        }

        var existing = await _context.ProductReviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);
        bool isUpdate = existing != null;

        if (existing != null)
        {
            existing.Rating = rating;
            existing.Comment = comment;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.ProductReviews.Add(new ProductReview
            {
                ProductId = productId,
                UserId = userId,
                Rating = rating,
                Comment = comment
            });
        }

        await _context.SaveChangesAsync();

        // Recompute the product's aggregate from real reviews now that this one is saved.
        var stats = await _context.ProductReviews
            .Where(r => r.ProductId == productId)
            .GroupBy(r => 1)
            .Select(g => new { Avg = g.Average(r => r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync();

        product.Rating = stats != null ? Math.Round((decimal)stats.Avg, 1) : 0m;
        product.ReviewCount = stats?.Count ?? 0;
        await _context.SaveChangesAsync();

        return (ReviewSubmitResult.Success, isUpdate);
    }

    private static string ReviewerName(ApplicationUser? user)
    {
        if (user == null)
        {
            return "Anonymous";
        }

        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return user.UserName ?? "Anonymous";
    }
}
