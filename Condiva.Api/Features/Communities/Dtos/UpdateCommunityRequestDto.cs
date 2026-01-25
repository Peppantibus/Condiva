namespace Condiva.Api.Features.Communities.Dtos;

public sealed record UpdateCommunityRequestDto(
    string Name,
    string Slug,
    string CreatedByUserId,
    string? Description);
