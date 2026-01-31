using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Memberships.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Items.Data;

public sealed class ItemRepository : IItemRepository
{
    private readonly CondivaDbContext _dbContext;

    public ItemRepository(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RepositoryResult<IReadOnlyList<Item>>> GetAllAsync(
        string communityId,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Item>>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return RepositoryResult<IReadOnlyList<Item>>.Failure(ApiErrors.Required(nameof(communityId)));
        }

        var items = await _dbContext.Items
            .Include(item => item.OwnerUser)
            .Where(item => item.CommunityId == communityId)
            .Where(item => _dbContext.Memberships.Any(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active
                && membership.CommunityId == communityId))
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Item>>.Success(items);
    }

    public async Task<RepositoryResult<Item>> GetByIdAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Unauthorized());
        }

        var item = await _dbContext.Items
            .Include(foundItem => foundItem.OwnerUser)
            .FirstOrDefaultAsync(foundItem => foundItem.Id == id);
        return item is null
            ? RepositoryResult<Item>.Failure(ApiErrors.NotFound("Item"))
            : await EnsureCommunityMemberAsync(item.CommunityId, actorUserId, item);
    }

    public async Task<RepositoryResult<Item>> CreateAsync(
        Item body,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.OwnerUserId))
        {
            body.OwnerUserId = actorUserId;
        }
        else if (!string.Equals(body.OwnerUserId, actorUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Invalid("OwnerUserId must match the current user."));
        }
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Required(nameof(body.Name)));
        }
        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var ownerExists = await _dbContext.Users
            .AnyAsync(user => user.Id == body.OwnerUserId);
        if (!ownerExists)
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Invalid("OwnerUserId does not exist."));
        }
        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.OwnerUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Item>.Failure(
                ApiErrors.Invalid("OwnerUserId is not a member of the community."));
        }
        if (string.IsNullOrWhiteSpace(body.Id))
        {
            body.Id = Guid.NewGuid().ToString();
        }
        body.CreatedAt = DateTime.UtcNow;
        body.UpdatedAt = null;

        _dbContext.Items.Add(body);
        await _dbContext.SaveChangesAsync();
        var createdItem = await _dbContext.Items
            .Include(item => item.OwnerUser)
            .FirstOrDefaultAsync(item => item.Id == body.Id);

        return RepositoryResult<Item>.Success(createdItem ?? body);
    }

    public async Task<RepositoryResult<Item>> UpdateAsync(
        string id,
        Item body,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Unauthorized());
        }
        var item = await _dbContext.Items.FindAsync(id);
        if (item is null)
        {
            return RepositoryResult<Item>.Failure(ApiErrors.NotFound("Item"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == item.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Item>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Item>.Failure(
                ApiErrors.Invalid("User is not allowed to update the item."));
        }
        if (item.Status is ItemStatus.Reserved or ItemStatus.InLoan)
        {
            return RepositoryResult<Item>.Failure(
                ApiErrors.Invalid("Item cannot be updated while reserved or in loan."));
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.OwnerUserId))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Required(nameof(body.OwnerUserId)));
        }
        if (!CanManageCommunity(membership)
            && !string.Equals(body.OwnerUserId, item.OwnerUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Invalid("OwnerUserId cannot be changed."));
        }
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Required(nameof(body.Name)));
        }
        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var ownerExists = await _dbContext.Users
            .AnyAsync(user => user.Id == body.OwnerUserId);
        if (!ownerExists)
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Invalid("OwnerUserId does not exist."));
        }
        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.OwnerUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Item>.Failure(
                ApiErrors.Invalid("OwnerUserId is not a member of the community."));
        }

        item.CommunityId = body.CommunityId;
        item.OwnerUserId = body.OwnerUserId;
        item.Name = body.Name;
        item.Description = body.Description;
        item.Category = body.Category;
        item.Status = body.Status;
        item.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        var updatedItem = await _dbContext.Items
            .Include(foundItem => foundItem.OwnerUser)
            .FirstOrDefaultAsync(foundItem => foundItem.Id == item.Id);

        return RepositoryResult<Item>.Success(updatedItem ?? item);
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
        var item = await _dbContext.Items.FindAsync(id);
        if (item is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Item"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == item.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not allowed to delete the item."));
        }
        if (item.Status is ItemStatus.Reserved or ItemStatus.InLoan)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("Item cannot be deleted while reserved or in loan."));
        }

        _dbContext.Items.Remove(item);
        await _dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    private async Task<RepositoryResult<Item>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        Item item)
    {
        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Item>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        return RepositoryResult<Item>.Success(item);
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }
}
