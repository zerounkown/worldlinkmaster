using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;

namespace WorldLinkMaster.Web.Areas.Support.Controllers;

public class ConversationsController : SupportBaseController
{
    private readonly ApplicationDbContext _context;

    public ConversationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string tab = "open")
    {
        var status = tab == "closed" ? ChatConversationStatus.Closed : ChatConversationStatus.Open;

        var conversations = await _context.ChatConversations
            .Include(c => c.Customer)
            .Include(c => c.AssignedAgent)
            .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1))
            .Where(c => c.Status == status)
            .OrderByDescending(c => c.Messages.Max(m => (DateTime?)m.SentAt) ?? c.CreatedAt)
            .ToListAsync();

        var items = conversations.Select(c => new ConversationListItemViewModel
        {
            Id = c.Id,
            CustomerName = $"{c.Customer?.FirstName} {c.Customer?.LastName}".Trim(),
            CustomerEmail = c.Customer?.Email ?? string.Empty,
            Status = c.Status,
            AssignedAgentName = c.AssignedAgent == null ? null : $"{c.AssignedAgent.FirstName} {c.AssignedAgent.LastName}".Trim(),
            CreatedAt = c.CreatedAt,
            LastMessageAt = c.Messages.Select(m => (DateTime?)m.SentAt).FirstOrDefault(),
            LastMessagePreview = c.Messages.Select(m => m.Body).FirstOrDefault()
        }).ToList();

        ViewBag.Tab = tab;
        return View(items);
    }

    public async Task<IActionResult> Details(int id)
    {
        var conversation = await _context.ChatConversations
            .Include(c => c.Customer)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (conversation == null)
        {
            return NotFound();
        }

        return View(conversation);
    }
}
