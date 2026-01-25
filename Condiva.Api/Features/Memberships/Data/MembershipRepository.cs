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
    public async Task<RepositoryResult<IReadOnlyList<Membership>>> GetAllAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Membership>>.Failure(ApiErrors.Unauthorized());
        }

        var actorCommunityIds = dbContext.Memberships
            .Where(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active)
            .Select(membership => membership.CommunityId);

        var memberships = await dbContext.Memberships
            .Where(membership => actorCommunityIds.Contains(membership.CommunityId))
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Membership>>.Success(memberships);
    }

    public async Task<RepositoryResult<IReadOnlyList<Community>>> GetMyCommunitiesAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Community>>.Failure(ApiErrors.Unauthorized());
        }

        var communities = await dbContext.Memberships
            .Where(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active)
            .Join(
                dbContext.Communities,
                membership => membership.CommunityId,
                community => community.Id,
                (_, community) => community)
            .Distinct()
            .ToListAsync();

        return RepositoryResult<IReadOnlyList<Community>>.Success(communities);
    }

    public async Task<RepositoryResult<Membership>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Unauthorized());
        }

        var membership = await dbContext.Memberships.FindAsync(id);
        return membership is null
            ? RepositoryResult<Membership>.Failure(ApiErrors.NotFound("Membership"))
            : await EnsureCommunityMemberAsync(membership.CommunityId, actorUserId, dbContext, membership);
    }

    public async Task<RepositoryResult<Membership>> CreateAsync(
        Membership body,
        string? enterCode,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
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
        var userExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!userExists)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("UserId does not exist."));
        }
        var community = await dbContext.Communities.FindAsync(body.CommunityId);
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
        var existingMembership = await dbContext.Memberships.AnyAsync(membership =>
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

        dbContext.Memberships.Add(body);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<Membership>.Success(body);
    }

    public async Task<RepositoryResult<Membership>> UpdateAsync(
        string id,
        Membership body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Unauthorized());
        }
        var membership = await dbContext.Memberships.FindAsync(id);
        if (membership is null)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.NotFound("Membership"));
        }
        var actorMembership = await dbContext.Memberships.FirstOrDefaultAsync(actor =>
            actor.CommunityId == membership.CommunityId
            && actor.UserId == actorUserId
            && actor.Status == MembershipStatus.Active);
        if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Invalid("User is not allowed to update membership."));
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
        var userExists = await dbContext.Users.AnyAsync(user => user.Id == body.UserId);
        if (!userExists)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("UserId does not exist."));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        if (!string.IsNullOrWhiteSpace(body.InvitedByUserId))
        {
            var inviterExists = await dbContext.Users
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

        await dbContext.SaveChangesAsync();
        return RepositoryResult<Membership>.Success(membership);
    }

    public async Task<RepositoryResult<Membership>> UpdateRoleAsync(
        string id,
        UpdateMembershipRoleRequest body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
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

        var membership = await dbContext.Memberships.FindAsync(id);
        if (membership is null)
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.NotFound("Membership"));
        }

        var actorMembership = await dbContext.Memberships.FirstOrDefaultAsync(actor =>
            actor.CommunityId == membership.CommunityId
            && actor.UserId == actorUserId
            && actor.Status == MembershipStatus.Active);
        if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Invalid("User is not allowed to change roles."));
        }
        if (membership.UserId == actorUserId)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Invalid("Owner role cannot be changed by the same user."));
        }
        if (membership.Role == MembershipRole.Owner && newRole != MembershipRole.Owner)
        {
            var otherOwners = await dbContext.Memberships.AnyAsync(other =>
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
        await dbContext.SaveChangesAsync();
        return RepositoryResult<Membership>.Success(membership);
    }

    public async Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        var membership = await dbContext.Memberships.FindAsync(id);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Membership"));
        }
        var actorMembership = await dbContext.Memberships.FirstOrDefaultAsync(actor =>
            actor.CommunityId == membership.CommunityId
            && actor.UserId == actorUserId
            && actor.Status == MembershipStatus.Active);
        if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not allowed to remove members."));
        }
        if (membership.Role == MembershipRole.Owner)
        {
            var canRemoveOwner = await HasAnotherActiveOwner(membership, dbContext);
            if (!canRemoveOwner)
            {
                return RepositoryResult<bool>.Failure(
                    ApiErrors.Invalid("At least one active owner is required."));
            }
        }

        dbContext.Memberships.Remove(membership);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    public async Task<RepositoryResult<bool>> LeaveAsync(
        string communityId,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
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

        var membership = await dbContext.Memberships.FirstOrDefaultAsync(member =>
            member.CommunityId == communityId
            && member.UserId == actorUserId
            && member.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Membership"));
        }
        if (membership.Role == MembershipRole.Owner)
        {
            var canLeaveOwner = await HasAnotherActiveOwner(membership, dbContext);
            if (!canLeaveOwner)
            {
                return RepositoryResult<bool>.Failure(
                    ApiErrors.Invalid("At least one active owner is required."));
            }
        }

        dbContext.Memberships.Remove(membership);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    private static async Task<bool> HasAnotherActiveOwner(
        Membership membership,
        CondivaDbContext dbContext)
    {
        return await dbContext.Memberships.AnyAsync(other =>
            other.CommunityId == membership.CommunityId
            && other.Role == MembershipRole.Owner
            && other.UserId != membership.UserId
            && other.Status == MembershipStatus.Active);
    }

    private static async Task<RepositoryResult<Membership>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        CondivaDbContext dbContext,
        Membership membership)
    {
        var isMember = await dbContext.Memberships.AnyAsync(existing =>
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
