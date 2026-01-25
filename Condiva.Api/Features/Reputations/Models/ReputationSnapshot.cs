namespace Condiva.Api.Features.Reputations.Models;

public sealed record ReputationSnapshot(
    string CommunityId,
    string UserId,
    int Score,
    int LendCount,
    int ReturnCount,
    int OnTimeReturnCount);
