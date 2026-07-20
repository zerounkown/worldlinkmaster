using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class SubcategoriesController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public SubcategoriesController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var subcategories = await _context.Subcategories
            .Include(s => s.Category)
            .Include(s => s.Products)
            .OrderBy(s => s.Category!.Name).ThenBy(s => s.Name)
            .ToListAsync();
        return View(subcategories);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Categories = new SelectList(await _context.Categories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        return View(new Subcategory());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Subcategory subcategory)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Categories = new SelectList(await _context.Categories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", subcategory.CategoryId);
            return View(subcategory);
        }

        _context.Subcategories.Add(subcategory);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Subcategory '{0}' created.", subcategory.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var subcategory = await _context.Subcategories.FindAsync(id);
        if (subcategory == null)
        {
            return NotFound();
        }

        ViewBag.Categories = new SelectList(await _context.Categories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", subcategory.CategoryId);
        return View(subcategory);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Subcategory subcategory)
    {
        if (id != subcategory.Id)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = new SelectList(await _context.Categories.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", subcategory.CategoryId);
            return View(subcategory);
        }

        _context.Subcategories.Update(subcategory);
        await _context.SaveChangesAsync();
        TempData["AdminMessage"] = _localizer["Subcategory '{0}' updated.", subcategory.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var subcategory = await _context.Subcategories.Include(s => s.Category).FirstOrDefaultAsync(s => s.Id == id);
        if (subcategory == null)
        {
            return NotFound();
        }

        return View(subcategory);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var hasProducts = await _context.Products.AnyAsync(p => p.SubcategoryId == id);
        if (hasProducts)
        {
            TempData["AdminMessage"] = _localizer["Cannot delete a subcategory that still has products assigned."].Value;
            return RedirectToAction(nameof(Index));
        }

        var subcategory = await _context.Subcategories.FindAsync(id);
        if (subcategory != null)
        {
            _context.Subcategories.Remove(subcategory);
            await _context.SaveChangesAsync();
            TempData["AdminMessage"] = _localizer["Subcategory '{0}' deleted.", subcategory.Name].Value;
        }

        return RedirectToAction(nameof(Index));
    }
}
