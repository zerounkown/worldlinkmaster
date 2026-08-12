using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class ReviewsController : Controller
{
    private readonly IProductReviewService _reviewService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ReviewsController(IProductReviewService reviewService, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResource> localizer)
    {
        _reviewService = reviewService;
        _userManager = userManager;
        _localizer = localizer;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int productId, int rating, string comment, string? slug, string? returnUrl)
    {
        var userId = _userManager.GetUserId(User)!;
        var (result, isUpdate) = await _reviewService.UpsertReviewAsync(productId, userId, rating, comment);

        var success = result == ReviewSubmitResult.Success;
        var message = result switch
        {
            ReviewSubmitResult.Success => isUpdate ? _localizer["Your review has been updated."] : _localizer["Thanks for your review!"],
            ReviewSubmitResult.InvalidRating => _localizer["Please choose a star rating from 1 to 5."],
            ReviewSubmitResult.InvalidCommentLength => _localizer["Reviews must be between 10 and 1000 characters."],
            ReviewSubmitResult.ProductNotFound => _localizer["That product couldn't be found."],
            _ => _localizer["Something went wrong. Please try again."]
        };

        if (IsAjax())
        {
            return Json(new { success, message, isUpdate });
        }

        TempData["ReviewMessage"] = message.Value;

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        if (!string.IsNullOrWhiteSpace(slug))
        {
            return RedirectToAction("Details", "Products", new { slug });
        }

        return RedirectToAction("Index", "Home");
    }

    private bool IsAjax() => Request.Headers["X-Requested-With"] == "XMLHttpRequest";
}
