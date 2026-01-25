namespace Condiva.Api.Features.Events.Dtos;

public sealed record UpdateEventRequestDto(
    string CommunityId,
    string EntityType,
    string EntityId,
    string Action,
    string? Payload);
