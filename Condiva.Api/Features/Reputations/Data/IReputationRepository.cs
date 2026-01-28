using Condiva.Api.Common.Results;
using Condiva.Api.Features.Reputations.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Reputations.Data;

public interface IReputationRepository
{
    Task<RepositoryResult<ReputationSnapshot>> GetMineAsync(
        string communityId,
        ClaimsPrincipal user);
    Task<RepositoryResult<ReputationSnapshot>> GetForUserAsync(
        string communityId,
        string userId,
        ClaimsPrincipal user);
}
