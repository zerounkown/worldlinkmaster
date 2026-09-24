using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

// The listing-page color filter facets on ColorFamily, not individual Colors — a Color with no
// FamilyId falls back into "Other" there instead of its own real family until an admin assigns
// one. New colors created by a product import auto-inherit a family when their name exactly
// matches an existing mapped one (see ApplicationDbContext.SaveChanges), but anything genuinely
// new still needs a human to pick a family the first time. This page is where that happens.
public class ColorsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ColorsController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer)
    {
        _context = context;
        _localizer = localizer;
    }

    public async Task<IActionResult> Index(bool unmappedOnly = false)
    {
        var query = _context.Colors
            .AsNoTracking()
            .Include(c => c.Family)
            .AsQueryable();

        if (unmappedOnly)
        {
            query = query.Where(c => c.FamilyId == null);
        }

        var colors = await query
            .OrderBy(c => c.FamilyId == null ? 0 : 1)
            .ThenBy(c => c.Name)
            .Select(c => new AdminColorRow
            {
                Id = c.Id,
                Name = c.Name,
                HexCode = c.HexCode,
                FamilyId = c.FamilyId,
                FamilyName = c.Family != null ? c.Family.Name : null,
                VariantProductCount = _context.ProductVariants.Where(v => v.ColorId == c.Id).Select(v => v.ProductId).Distinct().Count(),
                ProductColorCount = _context.ProductColors.Where(pc => pc.ColorId == c.Id).Select(pc => pc.ProductId).Distinct().Count()
            })
            .ToListAsync();

        ViewBag.UnmappedCount = await _context.Colors.CountAsync(c => c.FamilyId == null);
        ViewBag.UnmappedOnly = unmappedOnly;
        ViewBag.Families = new SelectList(await _context.ColorFamilies.OrderBy(f => f.DisplayOrder).ToListAsync(), "Id", "Name");

        return View(colors);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignFamily(int id, int? familyId, bool unmappedOnly = false)
    {
        var color = await _context.Colors.FindAsync(id);
        if (color == null)
        {
            return NotFound();
        }

        color.FamilyId = familyId;
        await _context.SaveChangesAsync();

        TempData["AdminMessage"] = familyId.HasValue
            ? _localizer["'{0}' assigned to a family.", color.Name].Value
            : _localizer["'{0}' set back to unmapped.", color.Name].Value;

        return RedirectToAction(nameof(Index), new { unmappedOnly });
    }
}
