using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Reputations.Models;

namespace Condiva.Api.Features.Reputations.Dtos;

public static class ReputationMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<ReputationSnapshot, ReputationDetailsDto>(snapshot => new ReputationDetailsDto(
            snapshot.CommunityId,
            snapshot.UserId,
            snapshot.Score,
            snapshot.LendCount,
            snapshot.ReturnCount,
            snapshot.OnTimeReturnCount,
            new ReputationWeightsDto(
                ReputationWeights.LendPoints,
                ReputationWeights.ReturnPoints,
                ReputationWeights.OnTimeReturnBonus)));
    }
}
