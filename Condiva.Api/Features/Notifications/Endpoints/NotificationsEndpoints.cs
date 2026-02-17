using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Notifications.Data;
using Condiva.Api.Features.Notifications.Dtos;
using Condiva.Api.Features.Notifications.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Notifications.Endpoints;

public static class NotificationsEndpoints
{
    private const int AvatarPresignTtlSeconds = 300;

    public static IEndpointRouteBuilder MapNotificationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/notifications");
        group.RequireAuthorization();
        group.WithTags("Notifications");

        group.MapGet("/", async (
            string? communityId,
            bool? unreadOnly,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            INotificationRepository repository,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
        {
            var result = await repository.GetPagedAsync(communityId, unreadOnly, page, pageSize, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var notifications = result.Data!.Items;
            var eventIds = notifications
                .Where(notification => !string.IsNullOrWhiteSpace(notification.EventId))
                .Select(notification => notification.EventId!)
                .Distinct()
                .ToList();
            var offerIds = notifications
                .Where(notification =>
                    string.Equals(notification.EntityType, "Offer", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(notification.EntityId))
                .Select(notification => notification.EntityId!)
                .Distinct()
                .ToList();
            var loanIds = notifications
                .Where(notification =>
                    string.Equals(notification.EntityType, "Loan", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(notification.EntityId))
                .Select(notification => notification.EntityId!)
                .Distinct()
                .ToList();
            var requestIds = notifications
                .Where(notification =>
                    string.Equals(notification.EntityType, "Request", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(notification.EntityId))
                .Select(notification => notification.EntityId!)
                .Distinct()
                .ToList();

            var eventsById = eventIds.Count == 0
                ? new Dictionary<string, Event>(StringComparer.Ordinal)
                : (await dbContext.Events
                    .AsNoTracking()
                    .Include(evt => evt.ActorUser)
                    .Where(evt => eventIds.Contains(evt.Id))
                    .ToListAsync())
                    .ToDictionary(evt => evt.Id, StringComparer.Ordinal);
            var offersById = offerIds.Count == 0
                ? new Dictionary<string, Offer>(StringComparer.Ordinal)
                : (await dbContext.Offers
                    .AsNoTracking()
                    .Include(offer => offer.Item)
                    .Include(offer => offer.Request)
                    .Where(offer => offerIds.Contains(offer.Id))
                    .ToListAsync())
                    .ToDictionary(offer => offer.Id, StringComparer.Ordinal);
            var loansById = loanIds.Count == 0
                ? new Dictionary<string, Loan>(StringComparer.Ordinal)
                : (await dbContext.Loans
                    .AsNoTracking()
                    .Include(loan => loan.Item)
                    .Where(loan => loanIds.Contains(loan.Id))
                    .ToListAsync())
                    .ToDictionary(loan => loan.Id, StringComparer.Ordinal);
            var requestsById = requestIds.Count == 0
                ? new Dictionary<string, Request>(StringComparer.Ordinal)
                : (await dbContext.Requests
                    .AsNoTracking()
                    .Where(request => requestIds.Contains(request.Id))
                    .ToListAsync())
                    .ToDictionary(request => request.Id, StringComparer.Ordinal);

            var items = notifications
                .Select(notification =>
                {
                    Event? evt = null;
                    if (!string.IsNullOrWhiteSpace(notification.EventId))
                    {
                        eventsById.TryGetValue(notification.EventId, out evt);
                    }

                    Offer? offer = null;
                    Loan? loan = null;
                    Request? request = null;
                    if (string.Equals(notification.EntityType, "Offer", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(notification.EntityId))
                    {
                        offersById.TryGetValue(notification.EntityId, out offer);
                    }
                    if (string.Equals(notification.EntityType, "Loan", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(notification.EntityId))
                    {
                        loansById.TryGetValue(notification.EntityId, out loan);
                    }
                    if (string.Equals(notification.EntityType, "Request", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(notification.EntityId))
                    {
                        requestsById.TryGetValue(notification.EntityId, out request);
                    }

                    var actor = BuildUserSummary(evt?.ActorUser, storageService);
                    var message = BuildMessage(notification.Type, actor?.DisplayName);
                    var entitySummary = BuildEntitySummary(notification, offer, loan, request);
                    var target = BuildTarget(notification, offer, loan);

                    return new NotificationListItemDto(
                        notification.Id,
                        notification.CommunityId,
                        notification.Type,
                        notification.EventId,
                        notification.EntityType,
                        notification.EntityId,
                        notification.Status,
                        notification.CreatedAt,
                        notification.ReadAt,
                        message,
                        actor,
                        entitySummary,
                        target);
                })
                .ToList();

            var payload = new PagedResponseDto<NotificationListItemDto>(
                items,
                result.Data!.Page,
                result.Data!.PageSize,
                result.Data!.Total,
                "createdAt",
                "desc");
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<NotificationListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            INotificationRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Notification, NotificationDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<NotificationDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/{id}/read", async (
            string id,
            ClaimsPrincipal user,
            INotificationRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.MarkReadAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Notification, NotificationDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<NotificationDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/read", async (
            NotificationMarkReadRequestDto body,
            ClaimsPrincipal user,
            INotificationRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.MarkReadAsync(body.Ids, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Notification, NotificationDetailsDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        })
            .Produces<List<NotificationDetailsDto>>(StatusCodes.Status200OK);

        group.MapGet("/unread-count", async (
            string? communityId,
            ClaimsPrincipal user,
            INotificationRepository repository) =>
        {
            var result = await repository.GetUnreadCountAsync(communityId, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.Ok(new UnreadCountDto(result.Data));
        })
            .Produces<UnreadCountDto>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static UserSummaryDto? BuildUserSummary(
        User? user,
        IR2StorageService storageService)
    {
        if (user is null)
        {
            return null;
        }

        var displayName = string.Empty;
        if (!string.IsNullOrWhiteSpace(user.Name) || !string.IsNullOrWhiteSpace(user.LastName))
        {
            displayName = $"{user.Name} {user.LastName}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(user.Username))
        {
            displayName = user.Username;
        }
        else
        {
            displayName = user.Id;
        }

        var avatarUrl = string.IsNullOrWhiteSpace(user.ProfileImageKey)
            ? null
            : storageService.GeneratePresignedGetUrl(user.ProfileImageKey, AvatarPresignTtlSeconds);
        return new UserSummaryDto(user.Id, displayName, user.Username ?? string.Empty, avatarUrl);
    }

    private static string BuildMessage(NotificationType type, string? actorDisplayName)
    {
        var actorLabel = string.IsNullOrWhiteSpace(actorDisplayName) ? "Qualcuno" : actorDisplayName;
        return type switch
        {
            NotificationType.OfferReceivedToRequester => $"{actorLabel} ha inviato una nuova offerta.",
            NotificationType.OfferAcceptedToLender => $"{actorLabel} ha accettato la tua offerta.",
            NotificationType.OfferRejectedToLender => $"{actorLabel} ha rifiutato la tua offerta.",
            NotificationType.OfferWithdrawnToRequester => $"{actorLabel} ha ritirato l'offerta.",
            NotificationType.LoanReservedToBorrower => "Un prestito e stato prenotato per te.",
            NotificationType.LoanReservedToLender => "Il tuo oggetto e stato prenotato.",
            NotificationType.LoanStartedToBorrower => "Il prestito e iniziato.",
            NotificationType.LoanReturnRequestedToLender => "Il borrower ha richiesto la restituzione.",
            NotificationType.LoanReturnConfirmedToBorrower => "La restituzione e stata confermata.",
            NotificationType.LoanReturnConfirmedToLender => "Hai confermato la restituzione.",
            NotificationType.LoanReturnCanceledToLender => "La richiesta di restituzione e stata annullata.",
            _ => "Nuova notifica."
        };
    }

    private static NotificationEntitySummaryDto? BuildEntitySummary(
        Notification notification,
        Offer? offer,
        Loan? loan,
        Request? request)
    {
        if (string.IsNullOrWhiteSpace(notification.EntityType)
            || string.IsNullOrWhiteSpace(notification.EntityId))
        {
            return null;
        }

        if (string.Equals(notification.EntityType, "Offer", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationEntitySummaryDto(
                "Offer",
                notification.EntityId,
                offer?.Item?.Name,
                offer?.Status.ToString());
        }

        if (string.Equals(notification.EntityType, "Loan", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationEntitySummaryDto(
                "Loan",
                notification.EntityId,
                loan?.Item?.Name,
                loan?.Status.ToString());
        }

        if (string.Equals(notification.EntityType, "Request", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationEntitySummaryDto(
                "Request",
                notification.EntityId,
                request?.Title,
                request?.Status.ToString());
        }

        return new NotificationEntitySummaryDto(
            notification.EntityType,
            notification.EntityId,
            null,
            null);
    }

    private static NotificationTargetDto? BuildTarget(
        Notification notification,
        Offer? offer,
        Loan? loan)
    {
        if (string.IsNullOrWhiteSpace(notification.EntityType)
            || string.IsNullOrWhiteSpace(notification.EntityId))
        {
            return null;
        }

        if (string.Equals(notification.EntityType, "Offer", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(offer?.RequestId))
            {
                return new NotificationTargetDto($"/requests/{offer.RequestId}", "Request", offer.RequestId);
            }

            return new NotificationTargetDto($"/offers/{notification.EntityId}", "Offer", notification.EntityId);
        }

        if (string.Equals(notification.EntityType, "Loan", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationTargetDto($"/loans/{notification.EntityId}", "Loan", notification.EntityId);
        }

        if (string.Equals(notification.EntityType, "Request", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationTargetDto($"/requests/{notification.EntityId}", "Request", notification.EntityId);
        }

        if (string.Equals(notification.EntityType, "Item", StringComparison.OrdinalIgnoreCase))
        {
            return new NotificationTargetDto($"/items/{notification.EntityId}", "Item", notification.EntityId);
        }

        return null;
    }

    public sealed record UnreadCountDto(int UnreadCount);
}
