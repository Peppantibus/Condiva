using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;

namespace Condiva.Api.Features.Notifications.Models;

public sealed class Notification
{
    public string Id { get; set; } = string.Empty;
    public string RecipientUserId { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string? EventId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Payload { get; set; }
    public NotificationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }

    public User? RecipientUser { get; set; }
    public Community? Community { get; set; }
}
