using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Reputations.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            return await GetReputation(communityId, actorUserId, actorUserId, dbContext);
        });

        group.MapGet("/{communityId}/users/{userId}", async (
            string communityId,
            string userId,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            return await GetReputation(communityId, userId, actorUserId, dbContext);
        });

        return endpoints;
    }

    private static async Task<IResult> GetReputation(
        string communityId,
        string targetUserId,
        string actorUserId,
        CondivaDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return ApiErrors.Required(nameof(communityId));
        }

        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == communityId);
        if (!communityExists)
        {
            return ApiErrors.Invalid("CommunityId does not exist.");
        }

        var actorIsMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId && membership.UserId == actorUserId);
        if (!actorIsMember)
        {
            return ApiErrors.Invalid("ActorUserId is not a member of the community.");
        }

        var targetExists = await dbContext.Users
            .AnyAsync(user => user.Id == targetUserId);
        if (!targetExists)
        {
            return ApiErrors.Invalid("UserId does not exist.");
        }

        var targetIsMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId && membership.UserId == targetUserId);
        if (!targetIsMember)
        {
            return ApiErrors.Invalid("UserId is not a member of the community.");
        }

        var reputation = await dbContext.Reputations
            .FirstOrDefaultAsync(profile =>
                profile.CommunityId == communityId && profile.UserId == targetUserId);

        if (reputation is null)
        {
            reputation = await ComputeAndStoreReputation(communityId, targetUserId, dbContext);
        }

        return Results.Ok(new ReputationResult(
            communityId,
            targetUserId,
            reputation.Score,
            reputation.LendCount,
            reputation.ReturnCount,
            reputation.OnTimeReturnCount,
            new ReputationWeightsResult(
                ReputationWeights.LendPoints,
                ReputationWeights.ReturnPoints,
                ReputationWeights.OnTimeReturnBonus)));
    }

    private static async Task<ReputationProfile> ComputeAndStoreReputation(
        string communityId,
        string userId,
        CondivaDbContext dbContext)
    {
        var lendsReturned = await dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.LenderUserId == userId
            && loan.Status == LoanStatus.Returned);

        var returnsReturned = await dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.BorrowerUserId == userId
            && loan.Status == LoanStatus.Returned);

        var returnsOnTime = await dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.BorrowerUserId == userId
            && loan.Status == LoanStatus.Returned
            && loan.DueAt != null
            && loan.ReturnedAt != null
            && loan.ReturnedAt <= loan.DueAt);

        var score = (lendsReturned * ReputationWeights.LendPoints)
            + (returnsReturned * ReputationWeights.ReturnPoints)
            + (returnsOnTime * ReputationWeights.OnTimeReturnBonus);

        var reputation = new ReputationProfile
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            UserId = userId,
            Score = score,
            LendCount = lendsReturned,
            ReturnCount = returnsReturned,
            OnTimeReturnCount = returnsOnTime,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Reputations.Add(reputation);
        await dbContext.SaveChangesAsync();

        return reputation;
    }

    public sealed record ReputationResult(
        string CommunityId,
        string UserId,
        int Score,
        int LendCount,
        int ReturnCount,
        int OnTimeReturnCount,
        ReputationWeightsResult Weights);

    public sealed record ReputationWeightsResult(
        int LendPoints,
        int ReturnPoints,
        int OnTimeReturnBonus);
}
