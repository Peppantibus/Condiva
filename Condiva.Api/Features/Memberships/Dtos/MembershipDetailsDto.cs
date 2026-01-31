using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Memberships.Dtos;

public sealed record MembershipDetailsDto(
    string Id,
    string UserId,
    string CommunityId,
    string Role,
    string Status,
    string? InvitedByUserId,
    DateTime CreatedAt,
    DateTime? JoinedAt,
    UserSummaryDto Users);
