using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Notifications.Data;
using Condiva.Api.Features.Notifications.Dtos;
using Condiva.Api.Features.Notifications.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Notifications.Endpoints;

public static class NotificationsEndpoints
{
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
            IMapper mapper) =>
        {
            var result = await repository.GetPagedAsync(communityId, unreadOnly, page, pageSize, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = new Condiva.Api.Common.Results.PagedResult<NotificationListItemDto>(
                mapper.MapList<Notification, NotificationListItemDto>(result.Data!.Items).ToList(),
                result.Data!.Page,
                result.Data!.PageSize,
                result.Data!.Total);
            return Results.Ok(payload);
        })
            .Produces<Condiva.Api.Common.Results.PagedResult<NotificationListItemDto>>(StatusCodes.Status200OK);

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

            return Results.Ok(new { count = result.Data });
        })
            .Produces(StatusCodes.Status200OK);

        return endpoints;
    }
}
