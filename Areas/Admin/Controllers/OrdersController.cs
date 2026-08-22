using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Resources;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Areas.Admin.Controllers;

public class OrdersController : AdminBaseController
{
    private readonly ApplicationDbContext _context;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly IEmailService _emailService;

    public OrdersController(ApplicationDbContext context, IStringLocalizer<SharedResource> localizer, IEmailService emailService)
    {
        _context = context;
        _localizer = localizer;
        _emailService = emailService;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.User)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        return View(orders);
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, OrderStatus status, string? trackingNumber)
    {
        var order = await _context.Orders.Include(o => o.User).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
        {
            return NotFound();
        }

        if (status == OrderStatus.Cancelled && (order.Status == OrderStatus.Delivered || order.Status == OrderStatus.Cancelled))
        {
            TempData["AdminError"] = _localizer["Order #{0} can't be cancelled — it's already {1}.", order.Id, _localizer[order.Status.ToString()].Value].Value;
            return RedirectToAction(nameof(Details), new { id });
        }

        var statusChanged = order.Status != status;
        order.Status = status;
        order.TrackingNumber = string.IsNullOrWhiteSpace(trackingNumber) ? null : trackingNumber.Trim();

        if (status == OrderStatus.Shipped && order.ShippedAt == null)
        {
            order.ShippedAt = DateTime.UtcNow;
        }
        if (status == OrderStatus.Delivered && order.DeliveredAt == null)
        {
            order.DeliveredAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        if (statusChanged && !string.IsNullOrWhiteSpace(order.User?.Email))
        {
            await _emailService.SendOrderStatusUpdateAsync(order, order.User.Email);
        }

        TempData["AdminMessage"] = _localizer["Order #{0} status updated to {1}.", order.Id, _localizer[status.ToString()].Value].Value;

        return RedirectToAction(nameof(Details), new { id });
    }
}
