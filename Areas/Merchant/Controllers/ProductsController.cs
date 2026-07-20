using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;

namespace WorldLinkMaster.Web.Areas.Merchant.Controllers;

public class ProductsController : MerchantBaseController
{
    private readonly IStringLocalizer<SharedResource> _localizer;

    public ProductsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IStringLocalizer<SharedResource> localizer)
        : base(context, userManager)
    {
        _localizer = localizer;
    }

    public async Task<IActionResult> Index()
    {
        var merchant = await GetCurrentMerchantAsync();
        if (merchant == null)
        {
            return RedirectToAction("Apply", "MerchantOnboarding", new { area = "" });
        }

        var products = await Context.Products
            .Include(p => p.Category)
            .Where(p => p.MerchantId == merchant.Id)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return View(products);
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Categories = await Context.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Subcategories = await Context.Subcategories.OrderBy(s => s.Name).ToListAsync();
        return View(new Product());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Product product)
    {
        var merchant = await GetCurrentMerchantAsync();
        if (merchant == null)
        {
            return RedirectToAction("Apply", "MerchantOnboarding", new { area = "" });
        }

        ModelState.Remove(nameof(Product.MerchantId));

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await Context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Subcategories = await Context.Subcategories.OrderBy(s => s.Name).ToListAsync();
            return View(product);
        }

        product.MerchantId = merchant.Id;
        product.CreatedAt = DateTime.UtcNow;
        Context.Products.Add(product);
        await Context.SaveChangesAsync();
        TempData["MerchantMessage"] = _localizer["Product '{0}' created.", product.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var merchant = await GetCurrentMerchantAsync();
        if (merchant == null)
        {
            return RedirectToAction("Apply", "MerchantOnboarding", new { area = "" });
        }

        var product = await Context.Products.FirstOrDefaultAsync(p => p.Id == id && p.MerchantId == merchant.Id);
        if (product == null)
        {
            return NotFound();
        }

        ViewBag.Categories = await Context.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Subcategories = await Context.Subcategories.OrderBy(s => s.Name).ToListAsync();
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Product product)
    {
        var merchant = await GetCurrentMerchantAsync();
        if (merchant == null)
        {
            return RedirectToAction("Apply", "MerchantOnboarding", new { area = "" });
        }

        var existing = await Context.Products.FirstOrDefaultAsync(p => p.Id == id && p.MerchantId == merchant.Id);
        if (existing == null)
        {
            return NotFound();
        }

        ModelState.Remove(nameof(Product.MerchantId));

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await Context.Categories.OrderBy(c => c.Name).ToListAsync();
            ViewBag.Subcategories = await Context.Subcategories.OrderBy(s => s.Name).ToListAsync();
            product.MerchantId = merchant.Id;
            return View(product);
        }

        existing.Name = product.Name;
        existing.Slug = product.Slug;
        existing.CategoryId = product.CategoryId;
        existing.SubcategoryId = product.SubcategoryId;
        existing.ShortDescription = product.ShortDescription;
        existing.Description = product.Description;
        existing.Price = product.Price;
        existing.WholesalePrice = product.WholesalePrice;
        existing.Sku = product.Sku;
        existing.StockQuantity = product.StockQuantity;
        existing.ImageUrl = product.ImageUrl;
        existing.IsFeatured = product.IsFeatured;

        await Context.SaveChangesAsync();
        TempData["MerchantMessage"] = _localizer["Product '{0}' updated.", existing.Name].Value;
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var merchant = await GetCurrentMerchantAsync();
        if (merchant == null)
        {
            return RedirectToAction("Apply", "MerchantOnboarding", new { area = "" });
        }

        var product = await Context.Products
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id && p.MerchantId == merchant.Id);

        if (product == null)
        {
            return NotFound();
        }

        return View(product);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var merchant = await GetCurrentMerchantAsync();
        if (merchant == null)
        {
            return RedirectToAction("Apply", "MerchantOnboarding", new { area = "" });
        }

        var product = await Context.Products.FirstOrDefaultAsync(p => p.Id == id && p.MerchantId == merchant.Id);
        if (product != null)
        {
            Context.Products.Remove(product);
            await Context.SaveChangesAsync();
            TempData["MerchantMessage"] = _localizer["Product '{0}' deleted.", product.Name].Value;
        }

        return RedirectToAction(nameof(Index));
    }
}
