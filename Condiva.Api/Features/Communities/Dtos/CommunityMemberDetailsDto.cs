using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CommunityMemberDetailsDto(
    string Id,
    string UserId,
    string CommunityId,
    string Role,
    string Status,
    DateTime? JoinedAt,
    UserSummaryDto User,
    CommunityMemberReputationSummaryDto ReputationSummary,
    string[] EffectivePermissions,
    string[]? AllowedActions = null);
