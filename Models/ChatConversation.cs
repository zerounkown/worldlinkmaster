namespace WorldLinkMaster.Web.Models;

public enum ChatConversationStatus
{
    Open,
    Closed
}

public class ChatConversation
{
    public int Id { get; set; }

    public string CustomerId { get; set; } = string.Empty;
    public ApplicationUser? Customer { get; set; }

    public string? AssignedAgentId { get; set; }
    public ApplicationUser? AssignedAgent { get; set; }

    public ChatConversationStatus Status { get; set; } = ChatConversationStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }

    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}
