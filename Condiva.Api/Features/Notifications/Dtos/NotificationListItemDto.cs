using Condiva.Api.Features.Notifications.Models;

namespace Condiva.Api.Features.Notifications.Dtos;

public sealed record NotificationListItemDto(
    string Id,
    string CommunityId,
    NotificationType Type,
    string? EventId,
    string? EntityType,
    string? EntityId,
    NotificationStatus Status,
    DateTime CreatedAt,
    DateTime? ReadAt);
