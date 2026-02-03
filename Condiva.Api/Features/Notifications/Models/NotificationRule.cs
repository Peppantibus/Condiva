namespace Condiva.Api.Features.Notifications.Models;

public sealed class NotificationRule
{
    public string EntityType { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
}
