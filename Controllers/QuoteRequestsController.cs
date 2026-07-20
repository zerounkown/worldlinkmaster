using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class QuoteRequestsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;

    public QuoteRequestsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IEmailService emailService)
    {
        _context = context;
        _userManager = userManager;
        _emailService = emailService;
    }

    public async Task<IActionResult> Create()
    {
        var model = new QuoteRequestViewModel();
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = await _userManager.GetUserAsync(User);
            model.Email = user?.Email ?? string.Empty;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(QuoteRequestViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var quoteRequest = new QuoteRequest
        {
            CompanyName = model.CompanyName,
            Email = model.Email,
            Phone = model.Phone,
            TradeLicenseNumber = model.TradeLicenseNumber,
            ProductDetails = model.ProductDetails,
            Quantity = model.Quantity,
            UserId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null
        };

        _context.QuoteRequests.Add(quoteRequest);
        await _context.SaveChangesAsync();

        await _emailService.SendQuoteRequestNotificationAsync(quoteRequest);

        return RedirectToAction(nameof(Confirmation), new { token = quoteRequest.ConfirmationToken });
    }

    public async Task<IActionResult> Confirmation(Guid token)
    {
        var quoteRequest = await _context.QuoteRequests.FirstOrDefaultAsync(q => q.ConfirmationToken == token);
        if (quoteRequest == null)
        {
            return NotFound();
        }

        return View(quoteRequest);
    }
}
