namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CreateCommunityRequestDto(
    string Name,
    string Slug,
    string CreatedByUserId,
    string? Description);
