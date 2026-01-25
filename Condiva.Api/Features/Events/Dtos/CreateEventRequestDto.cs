namespace Condiva.Api.Features.Events.Dtos;

public sealed record CreateEventRequestDto(
    string CommunityId,
    string EntityType,
    string EntityId,
    string Action,
    string? Payload);
