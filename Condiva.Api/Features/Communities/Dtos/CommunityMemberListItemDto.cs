using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Communities.Dtos;

public sealed record CommunityMemberReputationSummaryDto(
    int Score,
    int LendCount,
    int ReturnCount,
    int OnTimeReturnCount);

public sealed record CommunityMemberListItemDto(
    string Id,
    string UserId,
    string CommunityId,
    string Role,
    string Status,
    DateTime? JoinedAt,
    UserSummaryDto User,
    CommunityMemberReputationSummaryDto ReputationSummary,
    string[]? AllowedActions = null);
