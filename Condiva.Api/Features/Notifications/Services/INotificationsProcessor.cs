namespace Condiva.Api.Features.Notifications.Services;

public interface INotificationsProcessor
{
    Task ProcessBatchAsync(CancellationToken stoppingToken);
}
