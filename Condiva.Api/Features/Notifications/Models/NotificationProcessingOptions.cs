namespace Condiva.Api.Features.Notifications.Models;

public sealed class NotificationProcessingOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 100;
}
