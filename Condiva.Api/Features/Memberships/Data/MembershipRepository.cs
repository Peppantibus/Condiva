using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Memberships.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Memberships.Data;

public sealed class MembershipRepository : IMembershipRepository
{
    private readonly CondivaDbContext _dbContext;

    public MembershipRepository(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RepositoryResult<IReadOnlyList<Membership>>> GetAllAsync(
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Membership>>.Failure(ApiErrors.Unauthorized());
        }

        var actorCommunityIds = _dbContext.Memberships
            .Where(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active)
            .Select(membership => membership.CommunityId);

        var memberships = await _dbContext.Memberships
            .Include(membership => membership.User)
            .Where(membership => actorCommunityIds.Contains(membership.CommunityId))
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Membership>>.Success(memberships);
    }

    public async Task<RepositoryResult<IReadOnlyList<Membership>>> GetMineAsync(
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Membership>>.Failure(ApiErrors.Unauthorized());
        }

        var memberships = await _dbContext.Memberships
            .Include(membership => membership.User)
            .Where(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active)
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Membership>>.Success(memberships);
    }

    public async Task<RepositoryResult<IReadOnlyList<Community>>> GetMyCommunitiesAsync(
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Community>>.Failure(ApiErrors.Unauthorized());
        }

        var communities = await _dbContext.Memberships
            .Where(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active)
            .Join(
                _dbContext.Communities,
                membership => membership.CommunityId,
                community => community.Id,
                (_, community) => community)
            .Distinct()
            .ToListAsync();

        return RepositoryResult<IReadOnlyList<Community>>.Success(communities);
    }

    public async Task<RepositoryResult<Membership>> GetByIdAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Unauthorized());
        }

        var membership = await _dbContext.Memberships
            .Include(foundMembership => foundMembership.User)
            .FirstOrDefaultAsync(foundMembership => foundMembership.Id == id);
        return membership is null
            ? RepositoryResult<Membership>.Failure(ApiErrors.NotFound("Membership"))
            : await EnsureCommunityMemberAsync(membership.CommunityId, actorUserId, membership);
    }

    public async Task<RepositoryResult<Membership>> CreateAsync(
        Membership body,
        string? enterCode,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(enterCode))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Required(nameof(enterCode)));
        }
        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!userExists)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("UserId does not exist."));
        }
        var community = await _dbContext.Communities.FindAsync(body.CommunityId);
        if (community is null)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        if (!string.Equals(community.EnterCode, enterCode, StringComparison.Ordinal))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("EnterCode is invalid."));
        }
        if (community.EnterCodeExpiresAt <= DateTime.UtcNow)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("EnterCode has expired."));
        }
        var existingMembership = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId && membership.UserId == actorUserId);
        if (existingMembership)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Invalid("User is already a member of the community."));
        }
        if (string.IsNullOrWhiteSpace(body.Id))
        {
            body.Id = Guid.NewGuid().ToString();
        }
        body.UserId = actorUserId;
        body.Role = MembershipRole.Member;
        body.Status = MembershipStatus.Active;
        body.InvitedByUserId = null;
        body.CreatedAt = DateTime.UtcNow;
        body.JoinedAt = DateTime.UtcNow;

        _dbContext.Memberships.Add(body);
        await _dbContext.SaveChangesAsync();
        var createdMembership = await LoadMembershipWithUserAsync(body.Id);
        return RepositoryResult<Membership>.Success(createdMembership ?? body);
    }

    public async Task<RepositoryResult<Membership>> UpdateAsync(
        string id,
        Membership body,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Unauthorized());
        }
        var membership = await _dbContext.Memberships.FindAsync(id);
        if (membership is null)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.NotFound("Membership"));
        }
        var actorMembership = await _dbContext.Memberships.FirstOrDefaultAsync(actor =>
            actor.CommunityId == membership.CommunityId
            && actor.UserId == actorUserId
            && actor.Status == MembershipStatus.Active);
        if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Forbidden("User is not allowed to update membership."));
        }
        if (string.IsNullOrWhiteSpace(body.UserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Required(nameof(body.UserId)));
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (!string.Equals(body.UserId, membership.UserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("UserId cannot be changed."));
        }
        if (!string.Equals(body.CommunityId, membership.CommunityId, StringComparison.Ordinal))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("CommunityId cannot be changed."));
        }
        var userExists = await _dbContext.Users.AnyAsync(user => user.Id == body.UserId);
        if (!userExists)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("UserId does not exist."));
        }
        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        if (!string.IsNullOrWhiteSpace(body.InvitedByUserId))
        {
            var inviterExists = await _dbContext.Users
                .AnyAsync(user => user.Id == body.InvitedByUserId);
            if (!inviterExists)
            {
                return RepositoryResult<Membership>.Failure(
                    ApiErrors.Invalid("InvitedByUserId does not exist."));
            }
        }

        membership.UserId = body.UserId;
        membership.CommunityId = body.CommunityId;
        membership.Role = body.Role;
        membership.Status = body.Status;
        membership.InvitedByUserId = body.InvitedByUserId;
        membership.JoinedAt = body.JoinedAt;

        await _dbContext.SaveChangesAsync();
        var updatedMembership = await LoadMembershipWithUserAsync(membership.Id);
        return RepositoryResult<Membership>.Success(updatedMembership ?? membership);
    }

    public async Task<RepositoryResult<Membership>> UpdateRoleAsync(
        string id,
        UpdateMembershipRoleRequest body,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.Role))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Required(nameof(body.Role)));
        }
        if (!Enum.TryParse<MembershipRole>(body.Role, true, out var newRole))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("Invalid role."));
        }

        var membership = await _dbContext.Memberships.FindAsync(id);
        if (membership is null)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.NotFound("Membership"));
        }

        var actorMembership = await _dbContext.Memberships.FirstOrDefaultAsync(actor =>
            actor.CommunityId == membership.CommunityId
            && actor.UserId == actorUserId
            && actor.Status == MembershipStatus.Active);
        if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Forbidden("User is not allowed to change roles."));
        }
        if (membership.UserId == actorUserId)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Invalid("Owner role cannot be changed by the same user."));
        }
        if (membership.Role == MembershipRole.Owner && newRole != MembershipRole.Owner)
        {
            var otherOwners = await _dbContext.Memberships.AnyAsync(other =>
                other.CommunityId == membership.CommunityId
                && other.Role == MembershipRole.Owner
                && other.UserId != membership.UserId
                && other.Status == MembershipStatus.Active);
            if (!otherOwners)
            {
                return RepositoryResult<Membership>.Failure(
                    ApiErrors.Invalid("At least one active owner is required."));
            }
        }

        membership.Role = newRole;
        await _dbContext.SaveChangesAsync();
        var updatedMembership = await LoadMembershipWithUserAsync(membership.Id);
        return RepositoryResult<Membership>.Success(updatedMembership ?? membership);
    }

    public async Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        var membership = await _dbContext.Memberships.FindAsync(id);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Membership"));
        }
        var actorMembership = await _dbContext.Memberships.FirstOrDefaultAsync(actor =>
            actor.CommunityId == membership.CommunityId
            && actor.UserId == actorUserId
            && actor.Status == MembershipStatus.Active);
        if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Forbidden("User is not allowed to remove members."));
        }
        if (membership.Role == MembershipRole.Owner)
        {
            var canRemoveOwner = await HasAnotherActiveOwner(membership);
            if (!canRemoveOwner)
            {
                return RepositoryResult<bool>.Failure(
                    ApiErrors.Invalid("At least one active owner is required."));
            }
        }

        _dbContext.Memberships.Remove(membership);
        await _dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    public async Task<RepositoryResult<bool>> LeaveAsync(
        string communityId,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Required(nameof(communityId)));
        }

        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(member =>
            member.CommunityId == communityId
            && member.UserId == actorUserId
            && member.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Membership"));
        }
        if (membership.Role == MembershipRole.Owner)
        {
            var canLeaveOwner = await HasAnotherActiveOwner(membership);
            if (!canLeaveOwner)
            {
                return RepositoryResult<bool>.Failure(
                    ApiErrors.Invalid("At least one active owner is required."));
            }
        }

        _dbContext.Memberships.Remove(membership);
        await _dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    private async Task<bool> HasAnotherActiveOwner(
        Membership membership)
    {
        return await _dbContext.Memberships.AnyAsync(other =>
            other.CommunityId == membership.CommunityId
            && other.Role == MembershipRole.Owner
            && other.UserId != membership.UserId
            && other.Status == MembershipStatus.Active);
    }

    private async Task<Membership?> LoadMembershipWithUserAsync(string membershipId)
    {
        return await _dbContext.Memberships
            .Include(membership => membership.User)
            .FirstOrDefaultAsync(membership => membership.Id == membershipId);
    }

    private async Task<RepositoryResult<Membership>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        Membership membership)
    {
        var isMember = await _dbContext.Memberships.AnyAsync(existing =>
            existing.CommunityId == communityId
            && existing.UserId == actorUserId
            && existing.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        return RepositoryResult<Membership>.Success(membership);
    }
}
