using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Areas.Merchant.Controllers;

[Area("Merchant")]
[Authorize(Roles = "Merchant")]
public abstract class MerchantBaseController : Controller
{
    protected readonly ApplicationDbContext Context;
    protected readonly UserManager<ApplicationUser> UserManager;

    protected MerchantBaseController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        Context = context;
        UserManager = userManager;
    }

    protected async Task<Models.Merchant?> GetCurrentMerchantAsync()
    {
        var userId = UserManager.GetUserId(User);
        return await Context.Merchants.FirstOrDefaultAsync(m => m.UserId == userId);
    }
}
