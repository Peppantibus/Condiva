namespace Condiva.Api.Features.Reputations.Dtos;

public sealed record ReputationDetailsDto(
    string CommunityId,
    string UserId,
    int Score,
    int LendCount,
    int ReturnCount,
    int OnTimeReturnCount,
    ReputationWeightsDto Weights);
