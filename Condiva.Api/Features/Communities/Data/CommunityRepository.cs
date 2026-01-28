using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Communities.Data;

public sealed class CommunityRepository : ICommunityRepository
{
    private readonly CondivaDbContext _dbContext;
    private readonly ICurrentUser _currentUser;

    public CommunityRepository(CondivaDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }


    public async Task<RepositoryResult<IReadOnlyList<Community>>> GetAllAsync(
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
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


    public async Task<RepositoryResult<Community>> GetByIdAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Unauthorized());
        }

        var community = await _dbContext.Communities.FindAsync(id);
        return community is null
            ? RepositoryResult<Community>.Failure(ApiErrors.NotFound("Community"))
            : await EnsureCommunityMemberAsync(community, actorUserId);
    }


    public async Task<RepositoryResult<InviteCodeInfo>> GetInviteCodeAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<InviteCodeInfo>.Failure(ApiErrors.Unauthorized());
        }

        var community = await _dbContext.Communities.FindAsync(id);
        if (community is null)
        {
            return RepositoryResult<InviteCodeInfo>.Failure(ApiErrors.NotFound("Community"));
        }

        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == id
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);

        if (membership is null || !CanManageInvites(membership))
        {
            return RepositoryResult<InviteCodeInfo>.Failure(
                ApiErrors.Invalid("User is not allowed to manage invites."));
        }

        return RepositoryResult<InviteCodeInfo>.Success(
            new InviteCodeInfo(community.EnterCode, community.EnterCodeExpiresAt));
    }


    public async Task<RepositoryResult<InviteCodeInfo>> RotateInviteCodeAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<InviteCodeInfo>.Failure(ApiErrors.Unauthorized());
        }

        var community = await _dbContext.Communities.FindAsync(id);
        if (community is null)
        {
            return RepositoryResult<InviteCodeInfo>.Failure(ApiErrors.NotFound("Community"));
        }

        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == id
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);

        if (membership is null || membership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<InviteCodeInfo>.Failure(
                ApiErrors.Invalid("User is not allowed to rotate invites."));
        }

        community.EnterCode = CreateEnterCode();
        community.EnterCodeExpiresAt = CreateEnterCodeExpiry();
        await _dbContext.SaveChangesAsync();

        return RepositoryResult<InviteCodeInfo>.Success(
            new InviteCodeInfo(community.EnterCode, community.EnterCodeExpiresAt));
    }


    public async Task<RepositoryResult<Membership>> JoinAsync(
        string? enterCode,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Unauthorized());
        }
        var normalizedEnterCode = string.IsNullOrWhiteSpace(enterCode) ? null : enterCode.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEnterCode))
        {
            return RepositoryResult<Membership>.Failure(ApiErrors.Required("EnterCode"));
        }

        // TODO: Add a unique index on Community.EnterCode to avoid ambiguous joins.
        var community = await _dbContext.Communities
            .FirstOrDefaultAsync(c =>
                c.EnterCode == normalizedEnterCode
                && c.EnterCodeExpiresAt > DateTime.UtcNow);
        if (community is null)
        {
            var exists = await _dbContext.Communities
                .AnyAsync(c => c.EnterCode == normalizedEnterCode);
            return exists
                ? RepositoryResult<Membership>.Failure(ApiErrors.Invalid("EnterCode has expired."))
                : RepositoryResult<Membership>.Failure(ApiErrors.Invalid("EnterCode is invalid."));
        }

        var existingMembership = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == community.Id
            && membership.UserId == actorUserId);
        if (existingMembership)
        {
            return RepositoryResult<Membership>.Failure(
                ApiErrors.Invalid("User is already a member of the community."));
        }

        var member = new Membership
        {
            Id = Guid.NewGuid().ToString(),
            UserId = actorUserId,
            CommunityId = community.Id,
            Role = MembershipRole.Member,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            JoinedAt = DateTime.UtcNow
        };

        _dbContext.Memberships.Add(member);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            var membershipExists = await _dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == community.Id
                && membership.UserId == actorUserId);
            if (membershipExists)
            {
                return RepositoryResult<Membership>.Failure(
                    ApiErrors.Invalid("User is already a member of the community."));
            }

            throw;
        }
        return RepositoryResult<Membership>.Success(member);
    }


    public async Task<RepositoryResult<PagedResult<Request>>> GetRequestsFeedAsync(
        string id,
        string? status,
        int? page,
        int? pageSize,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Request>>.Failure(ApiErrors.Unauthorized());
        }

        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == id);
        if (!communityExists)
        {
            return RepositoryResult<PagedResult<Request>>.Failure(ApiErrors.NotFound("Community"));
        }

        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == id
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<PagedResult<Request>>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
        var requestStatus = RequestStatus.Open;
        if (!string.IsNullOrWhiteSpace(normalizedStatus))
        {
            if (!Enum.TryParse<RequestStatus>(normalizedStatus, true, out requestStatus))
            {
                return RepositoryResult<PagedResult<Request>>.Failure(
                    ApiErrors.Invalid("Invalid status filter."));
            }
        }

        var pageNumber = page.GetValueOrDefault(1);
        var size = pageSize.GetValueOrDefault(20);
        if (pageNumber <= 0 || size <= 0 || size > 100)
        {
            return RepositoryResult<PagedResult<Request>>.Failure(
                ApiErrors.Invalid("Invalid pagination parameters."));
        }

        var baseQuery = _dbContext.Requests
            .Where(request => request.CommunityId == id && request.Status == requestStatus);

        var total = await baseQuery.CountAsync();
        var items = await baseQuery
            .Include(request => request.Community)
            .Include(request => request.RequesterUser)
            .OrderByDescending(request => request.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync();

        return RepositoryResult<PagedResult<Request>>.Success(
            new PagedResult<Request>(items, pageNumber, size, total));
    }


    public async Task<RepositoryResult<PagedResult<Item>>> GetAvailableItemsAsync(
        string id,
        string? category,
        int? page,
        int? pageSize,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Item>>.Failure(ApiErrors.Unauthorized());
        }

        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == id);
        if (!communityExists)
        {
            return RepositoryResult<PagedResult<Item>>.Failure(ApiErrors.NotFound("Community"));
        }

        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == id
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<PagedResult<Item>>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        var pageNumber = page.GetValueOrDefault(1);
        var size = pageSize.GetValueOrDefault(20);
        if (pageNumber <= 0 || size <= 0 || size > 100)
        {
            return RepositoryResult<PagedResult<Item>>.Failure(
                ApiErrors.Invalid("Invalid pagination parameters."));
        }

        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        var baseQuery = _dbContext.Items
            .Where(item => item.CommunityId == id && item.Status == ItemStatus.Available);

        if (!string.IsNullOrWhiteSpace(normalizedCategory))
        {
            baseQuery = baseQuery.Where(item => item.Category == normalizedCategory);
        }

        var total = await baseQuery.CountAsync();
        var items = await baseQuery
            .Include(item => item.OwnerUser)
            .OrderByDescending(item => item.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync();

        return RepositoryResult<PagedResult<Item>>.Success(
            new PagedResult<Item>(items, pageNumber, size, total));
    }


    public async Task<RepositoryResult<Community>> CreateAsync(
        Community body,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Required(nameof(body.Name)));
        }
        if (string.IsNullOrWhiteSpace(body.Slug))
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Required(nameof(body.Slug)));
        }
        var creatorExists = await _dbContext.Users
            .AnyAsync(user => user.Id == actorUserId);
        if (!creatorExists)
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Invalid("Actor user does not exist."));
        }
        if (string.IsNullOrWhiteSpace(body.Id))
        {
            body.Id = Guid.NewGuid().ToString();
        }
        var now = DateTime.UtcNow;
        body.CreatedByUserId = actorUserId;
        body.EnterCode = CreateEnterCode();
        body.EnterCodeExpiresAt = CreateEnterCodeExpiry();
        body.CreatedAt = now;

        _dbContext.Communities.Add(body);
        _dbContext.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid().ToString(),
            UserId = actorUserId,
            CommunityId = body.Id,
            Role = MembershipRole.Owner,
            Status = MembershipStatus.Active,
            CreatedAt = now,
            JoinedAt = now
        });
        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Community>.Success(body);
    }

    private async Task<RepositoryResult<Community>> EnsureCommunityMemberAsync(
        Community community,
        string actorUserId)
    {
        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == community.Id
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Community>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        return RepositoryResult<Community>.Success(community);
    }


    public async Task<RepositoryResult<Community>> UpdateAsync(
        string id,
        Community body,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Unauthorized());
        }
        var community = await _dbContext.Communities.FindAsync(id);
        if (community is null)
        {
            return RepositoryResult<Community>.Failure(ApiErrors.NotFound("Community"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == id
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null || membership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<Community>.Failure(
                ApiErrors.Invalid("User is not allowed to update the community."));
        }
        if (string.IsNullOrWhiteSpace(body.Name))
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Required(nameof(body.Name)));
        }
        if (string.IsNullOrWhiteSpace(body.Slug))
        {
            return RepositoryResult<Community>.Failure(ApiErrors.Required(nameof(body.Slug)));
        }
        community.Name = body.Name;
        community.Slug = body.Slug;
        community.Description = body.Description;

        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Community>.Success(community);
    }


    public async Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        var community = await _dbContext.Communities.FindAsync(id);
        if (community is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Community"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == id
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null || membership.Role != MembershipRole.Owner)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not allowed to delete the community."));
        }

        // TODO: Ensure cascading delete or explicit cleanup for related entities.
        _dbContext.Communities.Remove(community);
        await _dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    private static bool CanManageInvites(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }

    private static string CreateEnterCode()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static DateTime CreateEnterCodeExpiry()
    {
        return DateTime.UtcNow.AddDays(7);
    }
}
