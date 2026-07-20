using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models;

public class ChatMessage
{
    public int Id { get; set; }

    public int ConversationId { get; set; }
    public ChatConversation? Conversation { get; set; }

    public string SenderUserId { get; set; } = string.Empty;
    public ApplicationUser? SenderUser { get; set; }

    [StringLength(150)]
    public string SenderName { get; set; } = string.Empty;

    public bool IsFromSupport { get; set; }

    [StringLength(2000)]
    public string Body { get; set; } = string.Empty;

    // Attachment (optional) — at most one file per message. StoragePath is an internal disk
    // key, never exposed to clients; downloads are served through a conversation-access-checked
    // controller action instead of a static file path.
    [StringLength(260)]
    public string? AttachmentStoragePath { get; set; }

    [StringLength(255)]
    public string? AttachmentFileName { get; set; }

    [StringLength(100)]
    public string? AttachmentContentType { get; set; }

    public long? AttachmentSizeBytes { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
