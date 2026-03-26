namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CommunityModerationTermDto(
    string Id,
    string Term,
    bool IsActive,
    string CreatedByUserId,
    DateTime CreatedAt);
