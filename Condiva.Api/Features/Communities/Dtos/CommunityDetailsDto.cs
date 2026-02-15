namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CommunityDetailsDto(
    string Id,
    string Name,
    string Slug,
    string? Description,
    string? ImageKey,
    string CreatedByUserId,
    DateTime CreatedAt);
