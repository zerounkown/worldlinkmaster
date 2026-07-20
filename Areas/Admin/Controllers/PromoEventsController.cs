using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class PromoEventsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public PromoEventsController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var events = await _context.PromoEvents.OrderBy(e => e.StartDate).ToListAsync();
        return View(events);
    }

    public IActionResult Create()
    {
        return View(new PromoEvent
        {
            StartDate = DateTime.UtcNow.Date,
            EndDate = DateTime.UtcNow.Date.AddDays(7)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PromoEvent promoEvent)
    {
        if (!ModelState.IsValid)
        {
            return View(promoEvent);
        }

        _context.PromoEvents.Add(promoEvent);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Event '{0}' created.", promoEvent.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var promoEvent = await _context.PromoEvents.FindAsync(id);
        if (promoEvent == null)
        {
            return NotFound();
        }

        return View(promoEvent);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PromoEvent promoEvent)
    {
        if (id != promoEvent.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(promoEvent);
        }

        _context.PromoEvents.Update(promoEvent);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Event '{0}' updated.", promoEvent.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var promoEvent = await _context.PromoEvents.FirstOrDefaultAsync(e => e.Id == id);
        if (promoEvent == null)
        {
            return NotFound();
        }

        return View(promoEvent);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var promoEvent = await _context.PromoEvents.FindAsync(id);
        if (promoEvent != null)
        {
            _context.PromoEvents.Remove(promoEvent);
            await _context.SaveChangesAsync();
            TempData["AdminMessage"] = _localizer["Event '{0}' deleted.", promoEvent.Name].Value;
        }

        return RedirectToAction(nameof(Index));
    }
}
