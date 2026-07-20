using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class QuoteRequestsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IEmailService _emailService;

    public QuoteRequestsController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer, IEmailService emailService)
    {
        _context = context;
        _localizer = localizer;
        _emailService = emailService;
    }

    public async Task<IActionResult> Index(QuoteStatus? status)
    {
        var query = _context.QuoteRequests.AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(q => q.Status == status.Value);
        }

        ViewBag.SelectedStatus = status;
        var requests = await query.OrderByDescending(q => q.CreatedAt).ToListAsync();
        return View(requests);
    }

    public async Task<IActionResult> Details(int id)
    {
        var request = await _context.QuoteRequests.FirstOrDefaultAsync(q => q.Id == id);
        if (request == null)
        {
            return NotFound();
        }

        return View(request);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, QuoteStatus status, decimal? quotedPrice, string? adminNotes)
    {
        var request = await _context.QuoteRequests.FirstOrDefaultAsync(q => q.Id == id);
        if (request == null)
        {
            return NotFound();
        }

        request.Status = status;
        request.QuotedPrice = quotedPrice;
        request.AdminNotes = adminNotes;
        request.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var confirmationUrl = Url.Action("Confirmation", "QuoteRequests", new { area = "", token = request.ConfirmationToken }, Request.Scheme);
        await _emailService.SendQuoteResponseAsync(request, confirmationUrl!);

        TempData["AdminMessage"] = _localizer["Quote request {0} updated.", $"WLM-Q-{request.Id:D6}"].Value;
        return RedirectToAction(nameof(Details), new { id });
    }
}
