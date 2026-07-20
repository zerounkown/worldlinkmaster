using WorldLinkMaster.Web.Models;

namespace WorldLinkMaster.Web.Models.ViewModels;

public class ConversationListItemViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public ChatConversationStatus Status { get; set; }
    public string? AssignedAgentName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessagePreview { get; set; }
}
