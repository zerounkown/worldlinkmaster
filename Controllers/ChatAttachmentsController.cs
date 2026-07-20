using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Hubs;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Controllers;

/// <summary>
/// Uploads/downloads for chat file &amp; photo attachments. Files are stored outside wwwroot
/// (never served by the static file middleware) and are only readable by someone with access
/// to the owning conversation — the same rule <see cref="ChatHub"/> enforces for messages.
/// </summary>
[Authorize]
[Route("chat/attachments")]
public class ChatAttachmentsController : Controller
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/webp",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain", "text/csv",
        "application/zip"
    };

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _environment;
    private readonly IHubContext<ChatHub> _hubContext;

    public ChatAttachmentsController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IWebHostEnvironment environment,
        IHubContext<ChatHub> hubContext)
    {
        _context = context;
        _userManager = userManager;
        _environment = environment;
        _hubContext = hubContext;
    }

    private bool IsSupportAgent => User.IsInRole("Admin") || User.IsInRole("Support");

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileSizeBytes + 1024)]
    public async Task<IActionResult> Upload([FromForm] int conversationId, [FromForm] IFormFile? file, [FromForm] string? caption)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file was uploaded.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return BadRequest("That file is too large (10 MB max).");
        }

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return BadRequest("That file type isn't supported. Try an image, PDF, Office document, text file, or zip.");
        }

        if (!await CanAccessConversationAsync(conversationId))
        {
            return Forbid();
        }

        var uploadsRoot = Path.Combine(_environment.ContentRootPath, "ChatUploads");
        Directory.CreateDirectory(uploadsRoot);

        var extension = Path.GetExtension(file.FileName);
        var storageFileName = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(uploadsRoot, storageFileName);

        await using (var stream = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }

        var userId = _userManager.GetUserId(User)!;
        var user = await _userManager.FindByIdAsync(userId);
        var isSupport = IsSupportAgent;

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderUserId = userId,
            SenderName = ChatHub.BuildDisplayName(user, isSupport),
            IsFromSupport = isSupport,
            Body = (caption ?? string.Empty).Trim(),
            AttachmentStoragePath = storageFileName,
            AttachmentFileName = Path.GetFileName(file.FileName),
            AttachmentContentType = file.ContentType,
            AttachmentSizeBytes = file.Length
        };
        _context.ChatMessages.Add(message);

        var conversation = await _context.ChatConversations.FindAsync(conversationId);
        if (conversation != null)
        {
            if (isSupport && conversation.AssignedAgentId == null)
            {
                conversation.AssignedAgentId = userId;
            }
            if (!isSupport && conversation.Status == ChatConversationStatus.Closed)
            {
                conversation.Status = ChatConversationStatus.Open;
                conversation.ClosedAt = null;
            }
        }

        await _context.SaveChangesAsync();

        var dto = ChatHub.ToDto(message);
        await _hubContext.Clients.Group($"conversation-{conversationId}").SendAsync("ReceiveMessage", conversationId, dto);
        if (!isSupport)
        {
            await _hubContext.Clients.Group("support-team").SendAsync("ConversationUpdated", conversationId);
        }

        return Ok(dto);
    }

    /// <summary>
    /// Serves an attachment. By default the response has no Content-Disposition, so opening it
    /// (e.g. in a new tab via target="_blank") lets the browser preview it in place — the chat
    /// page itself is never navigated away from. Pass <c>?download=true</c> to instead force a
    /// Save-As download (Content-Disposition: attachment).
    /// </summary>
    [HttpGet("{messageId:int}")]
    public async Task<IActionResult> Download(int messageId, bool download = false)
    {
        var message = await _context.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null || message.AttachmentStoragePath == null)
        {
            return NotFound();
        }

        if (!await CanAccessConversationAsync(message.ConversationId))
        {
            return Forbid();
        }

        var fullPath = Path.Combine(_environment.ContentRootPath, "ChatUploads", message.AttachmentStoragePath);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var contentType = message.AttachmentContentType ?? "application/octet-stream";
        return download
            ? PhysicalFile(fullPath, contentType, message.AttachmentFileName ?? "attachment")
            : PhysicalFile(fullPath, contentType);
    }

    private async Task<bool> CanAccessConversationAsync(int conversationId)
    {
        if (IsSupportAgent)
        {
            return true;
        }

        var userId = _userManager.GetUserId(User);
        return await _context.ChatConversations.AnyAsync(c => c.Id == conversationId && c.CustomerId == userId);
    }
}
