namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CommunityListItemDto(
    string Id,
    string Name,
    string Slug,
    string? Description,
    string CreatedByUserId,
    DateTime CreatedAt);
