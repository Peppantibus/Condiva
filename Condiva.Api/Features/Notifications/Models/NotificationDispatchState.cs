namespace Condiva.Api.Features.Notifications.Models;

public sealed class NotificationDispatchState
{
    public string Id { get; set; } = "default";
    public DateTime LastProcessedAt { get; set; }
    public string LastProcessedEventId { get; set; } = string.Empty;
}
