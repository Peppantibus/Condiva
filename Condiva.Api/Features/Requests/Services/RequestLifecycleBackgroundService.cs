using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Condiva.Api.Features.Requests.Services;

public sealed class RequestLifecycleBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RequestLifecycleBackgroundService> _logger;
    private readonly RequestLifecycleOptions _options;

    public RequestLifecycleBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RequestLifecycleBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = configuration.GetSection("RequestLifecycle")
            .Get<RequestLifecycleOptions>() ?? new RequestLifecycleOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(_options.PollIntervalSeconds, 1)));
        while (!stoppingToken.IsCancellationRequested
            && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Request lifecycle background service failed.");
            }
        }
    }

    private async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var storageService = scope.ServiceProvider.GetRequiredService<IR2StorageService>();
        var now = DateTime.UtcNow;

        await ExpireRequestsAsync(dbContext, now, cancellationToken);
        await ExpireOffersAsync(dbContext, now, cancellationToken);
        await CleanupRequestsAsync(dbContext, storageService, now, cancellationToken);
        await CleanupOffersAsync(dbContext, now, cancellationToken);
    }

    private async Task ExpireRequestsAsync(
        CondivaDbContext dbContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(_options.BatchSize, 1);
        var requests = await dbContext.Requests
            .Where(request =>
                request.Status == RequestStatus.Open
                && request.NeededTo.HasValue
                && request.NeededTo.Value < now)
            .OrderBy(request => request.NeededTo)
            .ThenBy(request => request.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (requests.Count == 0)
        {
            return;
        }

        foreach (var request in requests)
        {
            request.Status = RequestStatus.Expired;
            dbContext.Events.Add(CreateEvent(
                request.CommunityId,
                request.RequesterUserId,
                "Request",
                request.Id,
                "RequestExpired",
                now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Expired {Count} requests.", requests.Count);
    }

    private async Task ExpireOffersAsync(
        CondivaDbContext dbContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(_options.BatchSize, 1);
        var offers = await dbContext.Offers
            .Where(offer =>
                offer.Status == OfferStatus.Open
                && !string.IsNullOrWhiteSpace(offer.RequestId))
            .Join(
                dbContext.Requests,
                offer => offer.RequestId!,
                request => request.Id,
                (offer, request) => new { Offer = offer, Request = request })
            .Where(pair =>
                pair.Request.Status != RequestStatus.Open
                || (pair.Request.NeededTo.HasValue
                    && pair.Request.NeededTo.Value < now))
            .OrderBy(pair => pair.Offer.CreatedAt)
            .ThenBy(pair => pair.Offer.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (offers.Count == 0)
        {
            return;
        }

        foreach (var pair in offers)
        {
            pair.Offer.Status = OfferStatus.Expired;
            dbContext.Events.Add(CreateEvent(
                pair.Offer.CommunityId,
                pair.Offer.OffererUserId,
                "Offer",
                pair.Offer.Id,
                "OfferExpired",
                now));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Expired {Count} offers.", offers.Count);
    }

    private async Task CleanupRequestsAsync(
        CondivaDbContext dbContext,
        IR2StorageService storageService,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(_options.BatchSize, 1);
        var requestReopenCutoff = now.AddDays(-Math.Max(_options.RequestReopenWindowDays, 0));
        var terminalCleanupCutoff = now.AddDays(-Math.Max(_options.TerminalCleanupAfterDays, 0));

        var candidates = await dbContext.Requests
            .Where(request =>
                (request.Status == RequestStatus.Expired
                    && request.NeededTo.HasValue
                    && request.NeededTo.Value < requestReopenCutoff)
                || ((request.Status == RequestStatus.Closed || request.Status == RequestStatus.Canceled)
                    && request.CreatedAt < terminalCleanupCutoff))
            .OrderBy(request => request.CreatedAt)
            .ThenBy(request => request.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return;
        }

        var deletedImageKeys = new HashSet<string>(StringComparer.Ordinal);
        var deletedRequests = 0;
        var deletedOffers = 0;

        foreach (var request in candidates)
        {
            var relatedOffers = await dbContext.Offers
                .Where(offer => offer.RequestId == request.Id)
                .ToListAsync(cancellationToken);
            var relatedOfferIds = relatedOffers
                .Select(offer => offer.Id)
                .ToHashSet(StringComparer.Ordinal);

            var relatedLoans = await dbContext.Loans
                .Where(loan =>
                    loan.RequestId == request.Id
                    || (loan.OfferId != null && relatedOfferIds.Contains(loan.OfferId)))
                .ToListAsync(cancellationToken);

            if (HasActiveLoan(relatedLoans))
            {
                continue;
            }

            if ((request.Status == RequestStatus.Closed || request.Status == RequestStatus.Canceled)
                && ResolveLatestActivityAt(request, relatedLoans) >= terminalCleanupCutoff)
            {
                continue;
            }

            foreach (var loan in relatedLoans)
            {
                if (string.Equals(loan.RequestId, request.Id, StringComparison.Ordinal))
                {
                    loan.RequestId = null;
                }

                if (!string.IsNullOrWhiteSpace(loan.OfferId) && relatedOfferIds.Contains(loan.OfferId))
                {
                    loan.OfferId = null;
                }
            }

            if (relatedOffers.Count > 0)
            {
                dbContext.Offers.RemoveRange(relatedOffers);
                deletedOffers += relatedOffers.Count;
            }

            if (!string.IsNullOrWhiteSpace(request.ImageKey))
            {
                deletedImageKeys.Add(request.ImageKey);
            }

            dbContext.Requests.Remove(request);
            deletedRequests += 1;
        }

        if (deletedRequests == 0 && deletedOffers == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var objectKey in deletedImageKeys)
        {
            try
            {
                await storageService.DeleteObjectAsync(objectKey, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed deleting expired request image from R2. key: {ObjectKey}", objectKey);
            }
        }

        _logger.LogInformation(
            "Cleaned up {RequestCount} requests, {OfferCount} linked offers and {ImageCount} request images.",
            deletedRequests,
            deletedOffers,
            deletedImageKeys.Count);
    }

    private async Task CleanupOffersAsync(
        CondivaDbContext dbContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Max(_options.BatchSize, 1);
        var offerCleanupAfterDays = Math.Max(_options.OfferCleanupAfterDays, 0);
        var expiredRetentionDays = Math.Max(
            offerCleanupAfterDays,
            Math.Max(_options.OfferReopenWindowDays, 0));
        var minimumRetentionDays = Math.Min(offerCleanupAfterDays, expiredRetentionDays);
        var broadCutoff = now.AddDays(-minimumRetentionDays);

        var preCandidates = await dbContext.Offers
            .Where(offer =>
                (offer.Status == OfferStatus.Expired
                    || offer.Status == OfferStatus.Rejected
                    || offer.Status == OfferStatus.Withdrawn)
                && offer.CreatedAt < broadCutoff)
            .OrderBy(offer => offer.CreatedAt)
            .ThenBy(offer => offer.Id)
            .Take(batchSize * 5)
            .ToListAsync(cancellationToken);
        if (preCandidates.Count == 0)
        {
            return;
        }

        var preCandidateIds = preCandidates
            .Select(offer => offer.Id)
            .ToList();
        var requestIds = preCandidates
            .Where(offer => !string.IsNullOrWhiteSpace(offer.RequestId))
            .Select(offer => offer.RequestId!)
            .Distinct()
            .ToList();
        var requestsById = requestIds.Count == 0
            ? new Dictionary<string, Request>(StringComparer.Ordinal)
            : await dbContext.Requests
                .Where(request => requestIds.Contains(request.Id))
                .ToDictionaryAsync(request => request.Id, cancellationToken);
        var offerEvents = preCandidateIds.Count == 0
            ? new List<Event>()
            : await dbContext.Events
                .Where(evt =>
                    evt.EntityType == "Offer"
                    && preCandidateIds.Contains(evt.EntityId)
                    && (evt.Action == "OfferExpired"
                        || evt.Action == "OfferRejected"
                        || evt.Action == "OfferWithdrawn"))
                .ToListAsync(cancellationToken);
        var eventsByOfferId = offerEvents
            .GroupBy(evt => evt.EntityId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.Ordinal);

        var candidates = preCandidates
            .Where(offer =>
            {
                Request? request = null;
                if (!string.IsNullOrWhiteSpace(offer.RequestId))
                {
                    requestsById.TryGetValue(offer.RequestId!, out request);
                }

                var terminalAt = ResolveOfferTerminalAt(offer, request, eventsByOfferId);
                var retentionDays = offer.Status == OfferStatus.Expired
                    ? expiredRetentionDays
                    : offerCleanupAfterDays;
                var cutoff = now.AddDays(-retentionDays);
                return terminalAt < cutoff;
            })
            .Take(batchSize)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var candidateIds = candidates
            .Select(offer => offer.Id)
            .ToList();

        var activeLoanOfferIds = await dbContext.Loans
            .Where(loan =>
                loan.OfferId != null
                && candidateIds.Contains(loan.OfferId)
                && loan.Status != LoanStatus.Returned
                && loan.Status != LoanStatus.Expired)
            .Select(loan => loan.OfferId!)
            .ToHashSetAsync(cancellationToken);

        var terminalLoans = await dbContext.Loans
            .Where(loan =>
                loan.OfferId != null
                && candidateIds.Contains(loan.OfferId)
                && (loan.Status == LoanStatus.Returned || loan.Status == LoanStatus.Expired))
            .ToListAsync(cancellationToken);

        var removableOffers = candidates
            .Where(offer => !activeLoanOfferIds.Contains(offer.Id))
            .ToList();
        if (removableOffers.Count == 0 && terminalLoans.Count == 0)
        {
            return;
        }

        var removableOfferIds = removableOffers
            .Select(offer => offer.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var loan in terminalLoans)
        {
            if (!string.IsNullOrWhiteSpace(loan.OfferId) && removableOfferIds.Contains(loan.OfferId))
            {
                loan.OfferId = null;
            }
        }

        if (removableOffers.Count > 0)
        {
            dbContext.Offers.RemoveRange(removableOffers);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Cleaned up {Count} stale offers.", removableOffers.Count);
    }

    private static DateTime ResolveOfferTerminalAt(
        Offer offer,
        Request? request,
        IReadOnlyDictionary<string, List<Event>> eventsByOfferId)
    {
        return offer.Status switch
        {
            OfferStatus.Expired => ResolveLastActionAt(eventsByOfferId, offer.Id, "OfferExpired")
                ?? request?.NeededTo
                ?? offer.CreatedAt,
            OfferStatus.Rejected => ResolveLastActionAt(eventsByOfferId, offer.Id, "OfferRejected")
                ?? offer.CreatedAt,
            OfferStatus.Withdrawn => ResolveLastActionAt(eventsByOfferId, offer.Id, "OfferWithdrawn")
                ?? offer.CreatedAt,
            _ => offer.CreatedAt
        };
    }

    private static DateTime? ResolveLastActionAt(
        IReadOnlyDictionary<string, List<Event>> eventsByOfferId,
        string offerId,
        string action)
    {
        if (!eventsByOfferId.TryGetValue(offerId, out var events))
        {
            return null;
        }

        DateTime? latest = null;
        foreach (var evt in events)
        {
            if (!string.Equals(evt.Action, action, StringComparison.Ordinal))
            {
                continue;
            }

            if (!latest.HasValue || evt.CreatedAt > latest.Value)
            {
                latest = evt.CreatedAt;
            }
        }

        return latest;
    }

    private static bool HasActiveLoan(IReadOnlyList<Loan> loans)
    {
        return loans.Any(loan =>
            loan.Status != LoanStatus.Returned
            && loan.Status != LoanStatus.Expired);
    }

    private static DateTime ResolveLatestActivityAt(Request request, IReadOnlyList<Loan> relatedLoans)
    {
        var latest = request.NeededTo ?? request.CreatedAt;
        foreach (var loan in relatedLoans)
        {
            var timestamp = loan.ReturnedAt ?? loan.StartAt;
            if (timestamp > latest)
            {
                latest = timestamp;
            }
        }

        return latest;
    }

    private static Event CreateEvent(
        string communityId,
        string actorUserId,
        string entityType,
        string entityId,
        string action,
        DateTime createdAt)
    {
        return new Event
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            ActorUserId = actorUserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            CreatedAt = createdAt
        };
    }
}
