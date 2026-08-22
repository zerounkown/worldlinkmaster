using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class HomeBannersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public HomeBannersController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var banners = await _context.HomeBanners.AsNoTracking().OrderBy(b => b.DisplayOrder).ToListAsync();
        return View(banners);
    }

    public async Task<IActionResult> Create()
    {
        var nextOrder = await _context.HomeBanners.CountAsync();
        return View(new HomeBanner { DisplayOrder = nextOrder });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(HomeBanner banner)
    {
        if (!ModelState.IsValid)
        {
            return View(banner);
        }

        _context.HomeBanners.Add(banner);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Banner '{0}' created.", banner.TitleEn].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var banner = await _context.HomeBanners.FindAsync(id);
        if (banner == null)
        {
            return NotFound();
        }

        return View(banner);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, HomeBanner banner)
    {
        if (id != banner.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(banner);
        }

        _context.HomeBanners.Update(banner);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Banner '{0}' updated.", banner.TitleEn].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var banner = await _context.HomeBanners.FirstOrDefaultAsync(b => b.Id == id);
        if (banner == null)
        {
            return NotFound();
        }

        return View(banner);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var banner = await _context.HomeBanners.FindAsync(id);
        if (banner != null)
        {
            _context.HomeBanners.Remove(banner);
            await _context.SaveChangesAsync();
            TempData["AdminMessage"] = _localizer["Banner '{0}' deleted.", banner.TitleEn].Value;
        }

        return RedirectToAction(nameof(Index));
    }
}
