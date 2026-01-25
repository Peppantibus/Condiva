using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Reputations.Data;
using Condiva.Api.Features.Reputations.Dtos;
using Condiva.Api.Features.Reputations.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Reputations.Endpoints;

public static class ReputationsEndpoints
{
    public static IEndpointRouteBuilder MapReputationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/reputation");
        group.RequireAuthorization();
        group.WithTags("Reputation");

        group.MapGet("/{communityId}/me", async (
            string communityId,
            ClaimsPrincipal user,
            IReputationRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetMineAsync(communityId, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<ReputationSnapshot, ReputationDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapGet("/{communityId}/users/{userId}", async (
            string communityId,
            string userId,
            ClaimsPrincipal user,
            IReputationRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetForUserAsync(communityId, userId, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<ReputationSnapshot, ReputationDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        return endpoints;
    }
}
