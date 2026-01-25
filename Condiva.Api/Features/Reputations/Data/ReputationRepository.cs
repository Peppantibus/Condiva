using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Reputations.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Reputations.Data;

public sealed class ReputationRepository : IReputationRepository
{
    public async Task<RepositoryResult<ReputationSnapshot>> GetMineAsync(
        string communityId,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Unauthorized());
        }

        return await GetReputation(communityId, actorUserId, actorUserId, dbContext);
    }

    public async Task<RepositoryResult<ReputationSnapshot>> GetForUserAsync(
        string communityId,
        string userId,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Unauthorized());
        }

        return await GetReputation(communityId, userId, actorUserId, dbContext);
    }

    private static async Task<RepositoryResult<ReputationSnapshot>> GetReputation(
        string communityId,
        string targetUserId,
        string actorUserId,
        CondivaDbContext dbContext)
    {
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Required(nameof(communityId)));
        }

        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == communityId);
        if (!communityExists)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(
                ApiErrors.Invalid("CommunityId does not exist."));
        }

        var actorIsMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == actorUserId
            && membership.Status == Features.Memberships.Models.MembershipStatus.Active);
        if (!actorIsMember)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(
                ApiErrors.Invalid("ActorUserId is not a member of the community."));
        }

        var targetExists = await dbContext.Users
            .AnyAsync(user => user.Id == targetUserId);
        if (!targetExists)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Invalid("UserId does not exist."));
        }

        var targetIsMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == targetUserId
            && membership.Status == Features.Memberships.Models.MembershipStatus.Active);
        if (!targetIsMember)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(
                ApiErrors.Invalid("UserId is not a member of the community."));
        }

        var reputation = await dbContext.Reputations
            .FirstOrDefaultAsync(profile =>
                profile.CommunityId == communityId && profile.UserId == targetUserId);

        if (reputation is null)
        {
            reputation = await ComputeAndStoreReputation(communityId, targetUserId, dbContext);
        }

        return RepositoryResult<ReputationSnapshot>.Success(new ReputationSnapshot(
            communityId,
            targetUserId,
            reputation.Score,
            reputation.LendCount,
            reputation.ReturnCount,
            reputation.OnTimeReturnCount));
    }

    private static async Task<ReputationProfile> ComputeAndStoreReputation(
        string communityId,
        string userId,
        CondivaDbContext dbContext)
    {
        var lendsReturned = await dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.LenderUserId == userId
            && loan.Status == Features.Loans.Models.LoanStatus.Returned);

        var returnsReturned = await dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.BorrowerUserId == userId
            && loan.Status == Features.Loans.Models.LoanStatus.Returned);

        var returnsOnTime = await dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.BorrowerUserId == userId
            && loan.Status == Features.Loans.Models.LoanStatus.Returned
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

}
