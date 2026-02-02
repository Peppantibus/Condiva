using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Notifications.Models;

namespace Condiva.Api.Features.Notifications.Dtos;

public static class NotificationMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<Notification, NotificationListItemDto>(notification => new NotificationListItemDto(
            notification.Id,
            notification.CommunityId,
            notification.Type,
            notification.EventId,
            notification.EntityType,
            notification.EntityId,
            notification.Status,
            notification.CreatedAt,
            notification.ReadAt));

        registry.Register<Notification, NotificationDetailsDto>(notification => new NotificationDetailsDto(
            notification.Id,
            notification.CommunityId,
            notification.Type,
            notification.EventId,
            notification.EntityType,
            notification.EntityId,
            notification.Payload,
            notification.Status,
            notification.CreatedAt,
            notification.ReadAt));
    }
}
