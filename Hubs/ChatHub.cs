using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Hubs;

/// <summary>
/// Backs the site-wide customer support chat. Customers get one open conversation with the
/// support team; any Admin/Support agent can see and reply to any conversation.
/// </summary>
[Authorize]
public class ChatHub : Hub
{
    private const string SupportGroup = "support-team";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public ChatHub(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    private static string ConversationGroup(int conversationId) => $"conversation-{conversationId}";

    private bool IsSupportAgent => Context.User?.IsInRole("Admin") == true || Context.User?.IsInRole("Support") == true;

    public override async Task OnConnectedAsync()
    {
        if (IsSupportAgent)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SupportGroup);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Customers call this on widget open — returns their current open conversation, creating one if needed.</summary>
    public async Task<int> StartOrGetConversation()
    {
        var userId = _userManager.GetUserId(Context.User!)!;

        var conversation = await _context.ChatConversations
            .Where(c => c.CustomerId == userId && c.Status == ChatConversationStatus.Open)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (conversation == null)
        {
            conversation = new ChatConversation { CustomerId = userId };
            _context.ChatConversations.Add(conversation);
            await _context.SaveChangesAsync();

            var customer = await _userManager.FindByIdAsync(userId);
            await Clients.Group(SupportGroup).SendAsync("NewConversation", new
            {
                conversation.Id,
                CustomerName = BuildDisplayName(customer, isSupport: false),
                CustomerEmail = customer?.Email,
                conversation.CreatedAt
            });
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversation.Id));
        return conversation.Id;
    }

    /// <summary>Support agents call this to open a specific conversation's message stream.</summary>
    public async Task<List<ChatMessageDto>> JoinConversation(int conversationId)
    {
        if (!await CanAccessConversationAsync(conversationId))
        {
            throw new HubException("You don't have access to this conversation.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
        return await GetHistory(conversationId);
    }

    public async Task<List<ChatMessageDto>> GetHistory(int conversationId)
    {
        if (!await CanAccessConversationAsync(conversationId))
        {
            throw new HubException("You don't have access to this conversation.");
        }

        var messages = await _context.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        return messages.Select(ToDto).ToList();
    }

    public async Task SendMessage(int conversationId, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        if (!await CanAccessConversationAsync(conversationId))
        {
            throw new HubException("You don't have access to this conversation.");
        }

        var userId = _userManager.GetUserId(Context.User!)!;
        var user = await _userManager.FindByIdAsync(userId);
        var isSupport = IsSupportAgent;
        var trimmedBody = body.Trim();
        if (trimmedBody.Length > 2000)
        {
            trimmedBody = trimmedBody[..2000];
        }

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderUserId = userId,
            SenderName = BuildDisplayName(user, isSupport),
            IsFromSupport = isSupport,
            Body = trimmedBody
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
                // A customer messaging a closed conversation re-opens it.
                conversation.Status = ChatConversationStatus.Open;
                conversation.ClosedAt = null;
            }
        }

        await _context.SaveChangesAsync();

        var dto = ToDto(message);
        await Clients.Group(ConversationGroup(conversationId)).SendAsync("ReceiveMessage", conversationId, dto);

        if (!isSupport)
        {
            await Clients.Group(SupportGroup).SendAsync("ConversationUpdated", conversationId);
        }
    }

    public async Task CloseConversation(int conversationId)
    {
        if (!IsSupportAgent)
        {
            throw new HubException("Only support agents can close a conversation.");
        }

        var conversation = await _context.ChatConversations.FindAsync(conversationId);
        if (conversation == null)
        {
            return;
        }

        conversation.Status = ChatConversationStatus.Closed;
        conversation.ClosedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await Clients.Group(ConversationGroup(conversationId)).SendAsync("ConversationClosed", conversationId);
        await Clients.Group(SupportGroup).SendAsync("ConversationUpdated", conversationId);
    }

    /// <summary>Shared with <see cref="Controllers.ChatAttachmentsController"/> so attachment
    /// messages (created outside the hub, over plain HTTP) get identical sender names.</summary>
    public static string BuildDisplayName(ApplicationUser? user, bool isSupport)
    {
        var name = $"{user?.FirstName} {user?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = user?.Email?.Split('@')[0] ?? (isSupport ? "Support" : "Customer");
        }

        return isSupport ? $"{name} (Support)" : name;
    }

    /// <summary>Shared with <see cref="Controllers.ChatAttachmentsController"/> so both the hub
    /// and the attachment upload endpoint produce identical wire objects for clients.</summary>
    public static ChatMessageDto ToDto(ChatMessage m) => new(
        m.Id,
        m.SenderName,
        m.IsFromSupport,
        m.Body,
        m.SentAt,
        m.AttachmentFileName == null ? null : $"/chat/attachments/{m.Id}",
        m.AttachmentFileName,
        m.AttachmentContentType != null && m.AttachmentContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));

    private async Task<bool> CanAccessConversationAsync(int conversationId)
    {
        if (IsSupportAgent)
        {
            return true;
        }

        var userId = _userManager.GetUserId(Context.User!);
        return await _context.ChatConversations.AnyAsync(c => c.Id == conversationId && c.CustomerId == userId);
    }
}

public record ChatMessageDto(
    int Id,
    string SenderName,
    bool IsFromSupport,
    string Body,
    DateTime SentAt,
    string? AttachmentUrl,
    string? AttachmentFileName,
    bool IsImage);
