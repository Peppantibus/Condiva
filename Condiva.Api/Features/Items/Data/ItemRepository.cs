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

    public async Task<RepositoryResult<PagedResult<Item>>> GetAllAsync(
        string communityId,
        string? owner,
        string? status,
        string? category,
        string? search,
        string? sort,
        int? page,
        int? pageSize,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Item>>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return RepositoryResult<PagedResult<Item>>.Failure(ApiErrors.Required(nameof(communityId)));
        }

        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active
            && membership.CommunityId == communityId);
        if (!isMember)
        {
            return RepositoryResult<PagedResult<Item>>.Failure(
                ApiErrors.Forbidden("User is not a member of the community."));
        }

        ItemStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ItemStatus>(status, true, out var parsedStatus))
            {
                return RepositoryResult<PagedResult<Item>>.Failure(
                    ApiErrors.Invalid("Invalid status filter."));
            }

            statusFilter = parsedStatus;
        }

        var query = _dbContext.Items
            .Include(item => item.OwnerUser)
            .Where(item => item.CommunityId == communityId)
            .AsQueryable();

        var normalizedOwner = Normalize(owner);
        if (!string.IsNullOrWhiteSpace(normalizedOwner))
        {
            if (string.Equals(normalizedOwner, "me", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(item => item.OwnerUserId == actorUserId);
            }
            else
            {
                query = query.Where(item => item.OwnerUserId == normalizedOwner);
            }
        }

        if (statusFilter.HasValue)
        {
            query = query.Where(item => item.Status == statusFilter.Value);
        }

        var normalizedCategory = Normalize(category);
        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            query = query.Where(item => item.Category == normalizedCategory);
        }

        var normalizedSearch = Normalize(search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var searchLower = normalizedSearch.ToLowerInvariant();
            query = query.Where(item =>
                item.Name.ToLower().Contains(searchLower)
                || (item.Description ?? string.Empty).ToLower().Contains(searchLower)
                || (item.Category ?? string.Empty).ToLower().Contains(searchLower));
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sort) ? "createdAt_desc" : sort.Trim();
        switch (normalizedSort.ToLowerInvariant())
        {
            case "createdat_asc":
                query = query.OrderBy(item => item.CreatedAt);
                break;
            case "createdat_desc":
                query = query.OrderByDescending(item => item.CreatedAt);
                break;
            case "name_asc":
                query = query.OrderBy(item => item.Name);
                break;
            case "name_desc":
                query = query.OrderByDescending(item => item.Name);
                break;
            default:
                return RepositoryResult<PagedResult<Item>>.Failure(
                    ApiErrors.Invalid("Invalid sort parameter."));
        }

        var usePaging = page.HasValue || pageSize.HasValue;
        if (usePaging)
        {
            var pageNumber = page.GetValueOrDefault(1);
            var size = pageSize.GetValueOrDefault(20);
            if (pageNumber <= 0 || size <= 0 || size > 100)
            {
                return RepositoryResult<PagedResult<Item>>.Failure(
                    ApiErrors.Invalid("Invalid pagination parameters."));
            }

            var total = await query.CountAsync();
            var items = await query
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();

            return RepositoryResult<PagedResult<Item>>.Success(
                new PagedResult<Item>(items, pageNumber, size, total));
        }

        var allItems = await query.ToListAsync();
        return RepositoryResult<PagedResult<Item>>.Success(
            new PagedResult<Item>(allItems, 1, allItems.Count, allItems.Count));
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
                ApiErrors.Forbidden("User is not a member of the community."));
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
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Item>.Failure(
                ApiErrors.Forbidden("User is not allowed to update the item."));
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
        if (!string.IsNullOrWhiteSpace(body.OwnerUserId)
            && !string.Equals(body.OwnerUserId, item.OwnerUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Item>.Failure(ApiErrors.Invalid("OwnerUserId cannot be changed."));
        }
        body.OwnerUserId = item.OwnerUserId;
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
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Forbidden("User is not allowed to delete the item."));
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

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
