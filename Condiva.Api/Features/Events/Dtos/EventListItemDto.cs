namespace Condiva.Api.Features.Events.Dtos;

public sealed record EventListItemDto(
    string Id,
    string CommunityId,
    string ActorUserId,
    string EntityType,
    string EntityId,
    string Action,
    string? Payload,
    DateTime CreatedAt);
