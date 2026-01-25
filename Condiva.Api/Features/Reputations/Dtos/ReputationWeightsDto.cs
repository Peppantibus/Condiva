namespace Condiva.Api.Features.Reputations.Dtos;

public sealed record ReputationWeightsDto(
    int LendPoints,
    int ReturnPoints,
    int OnTimeReturnBonus);
