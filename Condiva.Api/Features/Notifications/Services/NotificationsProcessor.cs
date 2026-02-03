using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Notifications.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Features.Notifications.Services;

public sealed class NotificationsProcessor : INotificationsProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationProcessingOptions _options;

    public NotificationsProcessor(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _options = configuration.GetSection("NotificationProcessing")
            .Get<NotificationProcessingOptions>() ?? new NotificationProcessingOptions();
    }

    public async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var rules = scope.ServiceProvider.GetRequiredService<NotificationRules>();

        var state = await dbContext.NotificationDispatchStates.FindAsync(new object[] { "default" }, stoppingToken);
        if (state is null)
        {
            state = new NotificationDispatchState
            {
                Id = "default",
                LastProcessedAt = DateTime.MinValue,
                LastProcessedEventId = string.Empty
            };
            dbContext.NotificationDispatchStates.Add(state);
            await dbContext.SaveChangesAsync(stoppingToken);
        }

        var lastAt = state.LastProcessedAt;
        var lastId = state.LastProcessedEventId ?? string.Empty;

        var events = await dbContext.Events
            .Where(evt =>
                evt.CreatedAt > lastAt
                || (evt.CreatedAt == lastAt && string.Compare(evt.Id, lastId) > 0))
            .OrderBy(evt => evt.CreatedAt)
            .ThenBy(evt => evt.Id)
            .Take(_options.BatchSize)
            .ToListAsync(stoppingToken);

        if (events.Count == 0)
        {
            return;
        }

        var ruleMap = await rules.GetMapAsync(stoppingToken);
        var eventTypes = events.ToDictionary(
            evt => evt.Id,
            evt => rules.GetNotificationTypes(evt, ruleMap));
        var recipientsByEvent = await ResolveRecipientsAsync(dbContext, events, eventTypes, stoppingToken);
        var existingKeys = await LoadExistingNotificationKeysAsync(dbContext, events, stoppingToken);

        foreach (var evt in events)
        {
            if (!eventTypes.TryGetValue(evt.Id, out var types) || types.Count == 0)
            {
                continue;
            }

            if (!recipientsByEvent.TryGetValue(evt.Id, out var recipients))
            {
                continue;
            }

            foreach (var (type, recipientUserId) in recipients)
            {
                if (string.IsNullOrWhiteSpace(recipientUserId))
                {
                    continue;
                }

                var key = new NotificationKey(evt.Id, type, recipientUserId);
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                dbContext.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid().ToString(),
                    RecipientUserId = recipientUserId,
                    CommunityId = evt.CommunityId,
                    Type = type,
                    EventId = evt.Id,
                    EntityType = evt.EntityType,
                    EntityId = evt.EntityId,
                    Payload = evt.Payload,
                    Status = NotificationStatus.Pending,
                    CreatedAt = evt.CreatedAt
                });
            }
        }

        var lastEvent = events[^1];
        state.LastProcessedAt = lastEvent.CreatedAt;
        state.LastProcessedEventId = lastEvent.Id;
        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private static async Task<Dictionary<string, List<(NotificationType Type, string RecipientUserId)>>> ResolveRecipientsAsync(
        CondivaDbContext dbContext,
        IReadOnlyList<Event> events,
        IReadOnlyDictionary<string, IReadOnlyList<NotificationType>> eventTypes,
        CancellationToken stoppingToken)
    {
        var result = new Dictionary<string, List<(NotificationType, string)>>();
        var offerEventIds = events.Where(evt => evt.EntityType == "Offer")
            .Select(evt => evt.EntityId)
            .Distinct()
            .ToList();
        var loanEventIds = events.Where(evt => evt.EntityType == "Loan")
            .Select(evt => evt.EntityId)
            .Distinct()
            .ToList();

        var offers = await dbContext.Offers
            .AsNoTracking()
            .Where(offer => offerEventIds.Contains(offer.Id))
            .ToListAsync(stoppingToken);
        var offersById = offers.ToDictionary(offer => offer.Id, StringComparer.Ordinal);

        var requestIds = offers
            .Where(offer => !string.IsNullOrWhiteSpace(offer.RequestId))
            .Select(offer => offer.RequestId!)
            .Distinct()
            .ToList();
        var requests = await dbContext.Requests
            .AsNoTracking()
            .Where(request => requestIds.Contains(request.Id))
            .ToListAsync(stoppingToken);
        var requestsById = requests.ToDictionary(request => request.Id, StringComparer.Ordinal);

        var loans = await dbContext.Loans
            .AsNoTracking()
            .Where(loan => loanEventIds.Contains(loan.Id))
            .ToListAsync(stoppingToken);
        var loansById = loans.ToDictionary(loan => loan.Id, StringComparer.Ordinal);

        foreach (var evt in events)
        {
            if (!eventTypes.TryGetValue(evt.Id, out var types) || types.Count == 0)
            {
                continue;
            }

            var recipients = new List<(NotificationType, string)>();
            if (evt.EntityType == "Offer" && offersById.TryGetValue(evt.EntityId, out var offer))
            {
                foreach (var type in types)
                {
                    switch (type)
                    {
                        case NotificationType.OfferReceivedToRequester:
                        case NotificationType.OfferWithdrawnToRequester:
                            if (!string.IsNullOrWhiteSpace(offer.RequestId)
                                && requestsById.TryGetValue(offer.RequestId, out var request))
                            {
                                recipients.Add((type, request.RequesterUserId));
                            }
                            break;
                        case NotificationType.OfferAcceptedToLender:
                        case NotificationType.OfferRejectedToLender:
                            recipients.Add((type, offer.OffererUserId));
                            break;
                    }
                }
            }

            if (evt.EntityType == "Loan" && loansById.TryGetValue(evt.EntityId, out var loan))
            {
                foreach (var type in types)
                {
                    switch (type)
                    {
                        case NotificationType.LoanReservedToBorrower:
                        case NotificationType.LoanStartedToBorrower:
                        case NotificationType.LoanReturnConfirmedToBorrower:
                            recipients.Add((type, loan.BorrowerUserId));
                            break;
                        case NotificationType.LoanReservedToLender:
                        case NotificationType.LoanReturnRequestedToLender:
                        case NotificationType.LoanReturnCanceledToLender:
                        case NotificationType.LoanReturnConfirmedToLender:
                            recipients.Add((type, loan.LenderUserId));
                            break;
                    }
                }
            }

            if (recipients.Count > 0)
            {
                result[evt.Id] = recipients;
            }
        }

        return result;
    }

    private static async Task<HashSet<NotificationKey>> LoadExistingNotificationKeysAsync(
        CondivaDbContext dbContext,
        IReadOnlyList<Event> events,
        CancellationToken stoppingToken)
    {
        var eventIds = events.Select(evt => evt.Id).ToList();
        var keys = await dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.EventId != null && eventIds.Contains(notification.EventId))
            .Select(notification => new NotificationKey(
                notification.EventId!,
                notification.Type,
                notification.RecipientUserId))
            .ToListAsync(stoppingToken);

        return new HashSet<NotificationKey>(keys);
    }

    private readonly record struct NotificationKey(
        string EventId,
        NotificationType Type,
        string RecipientUserId);
}
