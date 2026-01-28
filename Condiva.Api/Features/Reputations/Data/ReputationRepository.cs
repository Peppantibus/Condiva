using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Reputations.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Reputations.Data;

public sealed class ReputationRepository : IReputationRepository
{
    private readonly CondivaDbContext _dbContext;
    private readonly ICurrentUser _currentUser;

    public ReputationRepository(CondivaDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<RepositoryResult<ReputationSnapshot>> GetMineAsync(
        string communityId,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Unauthorized());
        }

        return await GetReputation(communityId, actorUserId, actorUserId);
    }


    public async Task<RepositoryResult<ReputationSnapshot>> GetForUserAsync(
        string communityId,
        string userId,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Unauthorized());
        }

        return await GetReputation(communityId, userId, actorUserId);
    }

    private async Task<RepositoryResult<ReputationSnapshot>> GetReputation(
        string communityId,
        string targetUserId,
        string actorUserId)
    {
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Required(nameof(communityId)));
        }

        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == communityId);
        if (!communityExists)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(
                ApiErrors.Invalid("CommunityId does not exist."));
        }

        var actorIsMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == actorUserId
            && membership.Status == Features.Memberships.Models.MembershipStatus.Active);
        if (!actorIsMember)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(
                ApiErrors.Invalid("ActorUserId is not a member of the community."));
        }

        var targetExists = await _dbContext.Users
            .AnyAsync(user => user.Id == targetUserId);
        if (!targetExists)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(ApiErrors.Invalid("UserId does not exist."));
        }

        var targetIsMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == targetUserId
            && membership.Status == Features.Memberships.Models.MembershipStatus.Active);
        if (!targetIsMember)
        {
            return RepositoryResult<ReputationSnapshot>.Failure(
                ApiErrors.Invalid("UserId is not a member of the community."));
        }

        var reputation = await _dbContext.Reputations
            .FirstOrDefaultAsync(profile =>
                profile.CommunityId == communityId && profile.UserId == targetUserId);

        if (reputation is null)
        {
            reputation = await ComputeAndStoreReputation(communityId, targetUserId);
        }

        return RepositoryResult<ReputationSnapshot>.Success(new ReputationSnapshot(
            communityId,
            targetUserId,
            reputation.Score,
            reputation.LendCount,
            reputation.ReturnCount,
            reputation.OnTimeReturnCount));
    }

    private async Task<ReputationProfile> ComputeAndStoreReputation(
        string communityId,
        string userId)
    {
        var lendsReturned = await _dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.LenderUserId == userId
            && loan.Status == Features.Loans.Models.LoanStatus.Returned);

        var returnsReturned = await _dbContext.Loans.CountAsync(loan =>
            loan.CommunityId == communityId
            && loan.BorrowerUserId == userId
            && loan.Status == Features.Loans.Models.LoanStatus.Returned);

        var returnsOnTime = await _dbContext.Loans.CountAsync(loan =>
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

        _dbContext.Reputations.Add(reputation);
        await _dbContext.SaveChangesAsync();

        return reputation;
    }

}
