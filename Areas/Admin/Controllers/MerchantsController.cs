using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class MerchantsController : AdminBaseController
{
    private readonly ApplicationDbContext _context;

    public MerchantsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var merchants = await _context.Merchants
            .Include(m => m.User)
            .Include(m => m.Products)
            .OrderBy(m => m.BusinessName)
            .ToListAsync();

        return View(merchants);
    }
}
