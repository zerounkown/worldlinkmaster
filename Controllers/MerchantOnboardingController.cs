using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

[Authorize]
public class MerchantOnboardingController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IStripeConnectService _stripeConnect;
    private readonly IConfiguration _configuration;

    public MerchantOnboardingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IStripeConnectService stripeConnect, IConfiguration configuration)
    {
        _context = context;
        _userManager = userManager;
        _signInManager = signInManager;
        _stripeConnect = stripeConnect;
        _configuration = configuration;
    }

    public async Task<IActionResult> Apply()
    {
        var userId = _userManager.GetUserId(User)!;
        var existing = await _context.Merchants.FirstOrDefaultAsync(m => m.UserId == userId);
        if (existing != null)
        {
            return RedirectToAction(nameof(StartOnboarding));
        }

        return View(new MerchantApplyViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Apply(MerchantApplyViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = _userManager.GetUserId(User)!;
        var existing = await _context.Merchants.FirstOrDefaultAsync(m => m.UserId == userId);
        if (existing != null)
        {
            return RedirectToAction(nameof(StartOnboarding));
        }

        var slug = SlugFrom(model.BusinessName);
        var slugTaken = await _context.Merchants.AnyAsync(m => m.Slug == slug);
        if (slugTaken)
        {
            slug = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        }

        var merchant = new Merchant
        {
            UserId = userId,
            BusinessName = model.BusinessName,
            Slug = slug,
            Description = model.Description
        };

        _context.Merchants.Add(merchant);
        await _context.SaveChangesAsync();

        var user = (await _userManager.GetUserAsync(User))!;
        await _userManager.AddToRoleAsync(user, "Merchant");

        // The signed-in cookie was issued before this role existed on the account -
        // refresh it now so [Authorize(Roles="Merchant")] recognizes it immediately.
        await _signInManager.RefreshSignInAsync(user);

        return RedirectToAction(nameof(StartOnboarding));
    }

    public async Task<IActionResult> StartOnboarding()
    {
        var userId = _userManager.GetUserId(User)!;
        var merchant = await _context.Merchants.FirstOrDefaultAsync(m => m.UserId == userId);
        if (merchant == null)
        {
            return RedirectToAction(nameof(Apply));
        }

        if (string.IsNullOrEmpty(_configuration["Stripe:SecretKey"]))
        {
            ViewBag.Merchant = merchant;
            return View("StripeNotConfigured");
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        try
        {
            if (string.IsNullOrEmpty(merchant.StripeAccountId))
            {
                var user = await _userManager.GetUserAsync(User);
                merchant.StripeAccountId = await _stripeConnect.CreateExpressAccountAsync(user!.Email!);
                await _context.SaveChangesAsync();
            }

            var onboardingUrl = await _stripeConnect.CreateOnboardingLinkAsync(
                merchant.StripeAccountId!,
                returnUrl: $"{baseUrl}/MerchantOnboarding/OnboardingReturn",
                refreshUrl: $"{baseUrl}/MerchantOnboarding/StartOnboarding");

            return Redirect(onboardingUrl);
        }
        catch (Stripe.StripeException ex)
        {
            ViewBag.Merchant = merchant;
            ViewBag.StripeError = ex.StripeError?.Message ?? ex.Message;
            return View("StripeNotConfigured");
        }
    }

    public async Task<IActionResult> OnboardingReturn()
    {
        var userId = _userManager.GetUserId(User)!;
        var merchant = await _context.Merchants.FirstOrDefaultAsync(m => m.UserId == userId);
        if (merchant == null || string.IsNullOrEmpty(merchant.StripeAccountId))
        {
            return RedirectToAction(nameof(Apply));
        }

        try
        {
            merchant.StripeOnboardingComplete = await _stripeConnect.IsOnboardingCompleteAsync(merchant.StripeAccountId);
            await _context.SaveChangesAsync();
        }
        catch (Stripe.StripeException)
        {
            // Leave onboarding status as-is; the merchant dashboard will show it's still incomplete.
        }

        return RedirectToAction("Index", "Home", new { area = "Merchant" });
    }

    private static string SlugFrom(string businessName)
    {
        var slug = businessName.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        return slug.Trim('-');
    }
}
