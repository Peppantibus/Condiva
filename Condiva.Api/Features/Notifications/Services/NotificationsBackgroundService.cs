using Condiva.Api.Features.Notifications.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Condiva.Api.Features.Notifications.Services;

public sealed class NotificationsBackgroundService : BackgroundService
{
    private readonly ILogger<NotificationsBackgroundService> _logger;
    private readonly NotificationProcessingOptions _options;
    private readonly INotificationsProcessor _processor;

    public NotificationsBackgroundService(
        INotificationsProcessor processor,
        IConfiguration configuration,
        ILogger<NotificationsBackgroundService> logger)
    {
        _processor = processor;
        _logger = logger;
        _options = configuration.GetSection("NotificationProcessing")
            .Get<NotificationProcessingOptions>() ?? new NotificationProcessingOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested
            && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notifications background service failed.");
            }
        }
    }

}
