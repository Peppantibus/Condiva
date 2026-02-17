using Condiva.Api.Features.Notifications.Models;
using Condiva.Api.Common.Dtos;

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
    DateTime? ReadAt,
    string Message,
    UserSummaryDto? Actor,
    NotificationEntitySummaryDto? EntitySummary,
    NotificationTargetDto? Target);
