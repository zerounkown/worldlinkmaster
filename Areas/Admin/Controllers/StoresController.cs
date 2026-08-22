using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class StoresController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public StoresController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var stores = await _context.Stores.AsNoTracking().OrderBy(s => s.DisplayOrder).ToListAsync();
        return View(stores);
    }

    public async Task<IActionResult> Create()
    {
        var nextOrder = await _context.Stores.CountAsync();
        return View(new Store { DisplayOrder = nextOrder });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Store store)
    {
        if (!ModelState.IsValid)
        {
            return View(store);
        }

        _context.Stores.Add(store);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Store '{0}' created.", store.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var store = await _context.Stores.FindAsync(id);
        if (store == null)
        {
            return NotFound();
        }

        return View(store);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Store store)
    {
        if (id != store.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(store);
        }

        _context.Stores.Update(store);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Store '{0}' updated.", store.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var store = await _context.Stores.FirstOrDefaultAsync(s => s.Id == id);
        if (store == null)
        {
            return NotFound();
        }

        return View(store);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var store = await _context.Stores.FindAsync(id);
        if (store != null)
        {
            _context.Stores.Remove(store);
            await _context.SaveChangesAsync();
            TempData["AdminMessage"] = _localizer["Store '{0}' deleted.", store.Name].Value;
        }

        return RedirectToAction(nameof(Index));
    }
}
