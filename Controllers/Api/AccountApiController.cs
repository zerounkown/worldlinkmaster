using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;

namespace WorldLinkMaster.Web.Controllers.Api;

[ApiController]
[Route("api/account")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class AccountApiController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AccountApiController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            userId = User.FindFirstValue(ClaimTypes.NameIdentifier),
            email = User.FindFirstValue(ClaimTypes.Email),
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value)
        });
    }

    [HttpGet("orders")]
    public async Task<IActionResult> MyOrders()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var orders = await _context.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new
            {
                o.Id,
                o.OrderDate,
                Status = o.Status.ToString(),
                o.Total,
                Currency = "AED",
                o.IsPaid
            })
            .ToListAsync();

        return Ok(orders);
    }
}
