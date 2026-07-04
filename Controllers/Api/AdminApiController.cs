using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.Web.Controllers.Api;

[ApiController]
[Route("api/admin")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public class AdminApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AdminApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        return Ok(new
        {
            productCount = await _context.Products.CountAsync(),
            orderCount = await _context.Orders.CountAsync(),
            merchantCount = await _context.Merchants.CountAsync(),
            paidOrderTotal = await _context.Orders.Where(o => o.IsPaid).SumAsync(o => (decimal?)o.Total) ?? 0
        });
    }
}
