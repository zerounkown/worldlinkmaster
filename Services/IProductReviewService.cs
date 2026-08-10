using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Services;

public enum ReviewSubmitResult
{
    Success,
    InvalidRating,
    InvalidCommentLength,
    ProductNotFound
}

public interface IProductReviewService
{
    Task<List<ProductReviewDisplay>> GetReviewsAsync(int productId);
    Task<ProductReview?> GetUserReviewAsync(int productId, string userId);
    Task<(ReviewSubmitResult result, bool isUpdate)> UpsertReviewAsync(int productId, string userId, int rating, string comment);
}
