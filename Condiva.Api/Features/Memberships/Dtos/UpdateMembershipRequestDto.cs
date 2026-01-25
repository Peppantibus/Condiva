namespace Condiva.Api.Features.Memberships.Dtos;

public sealed record UpdateMembershipRequestDto(
    string UserId,
    string CommunityId,
    string Role,
    string Status,
    string? InvitedByUserId,
    DateTime? JoinedAt);
