namespace Condiva.Api.Features.Memberships.Dtos;

public sealed record MyCommunityContextListItemDto(
    string CommunityId,
    string Name,
    string Slug,
    string? Description,
    string? ImageKey,
    string MembershipId,
    string Role,
    string Status,
    DateTime? JoinedAt,
    string[] CommunityAllowedActions,
    string[] MembershipAllowedActions);
