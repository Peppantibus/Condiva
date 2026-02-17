namespace Condiva.Api.Features.Notifications.Dtos;

public sealed record NotificationEntitySummaryDto(
    string EntityType,
    string EntityId,
    string? Label,
    string? Status);
