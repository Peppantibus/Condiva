namespace Condiva.Api.Features.Memberships.Dtos;

public sealed record MembershipListItemDto(
    string Id,
    string UserId,
    string CommunityId,
    string Role,
    string Status,
    string? InvitedByUserId,
    DateTime CreatedAt,
    DateTime? JoinedAt);
