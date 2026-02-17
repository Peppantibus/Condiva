namespace Condiva.Api.Features.Notifications.Dtos;

public sealed record NotificationTargetDto(
    string Route,
    string EntityType,
    string EntityId);
