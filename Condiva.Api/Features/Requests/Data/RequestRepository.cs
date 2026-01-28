using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Requests.Data;

public sealed class RequestRepository : IRequestRepository
{
    private readonly ICurrentUser _currentUser;

    public RequestRepository(ICurrentUser currentUser)
    {
        _currentUser = currentUser;
    }
    private const int MaxDailyRequestsPerUser = 3;
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(8);

    public async Task<RepositoryResult<IReadOnlyList<Request>>> GetAllAsync(
        string communityId,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Request>>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return RepositoryResult<IReadOnlyList<Request>>.Failure(ApiErrors.Required(nameof(communityId)));
        }

        var requests = await dbContext.Requests
            .Include(request => request.Community)
            .Include(request => request.RequesterUser)
            .Where(request => request.CommunityId == communityId)
            .Where(request => dbContext.Memberships.Any(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active
                && membership.CommunityId == communityId))
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Request>>.Success(requests);
    }


    public async Task<RepositoryResult<Request>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Unauthorized());
        }

        var request = await dbContext.Requests
            .Include(item => item.Community)
            .Include(item => item.RequesterUser)
            .FirstOrDefaultAsync(item => item.Id == id);
        return request is null
            ? RepositoryResult<Request>.Failure(ApiErrors.NotFound("Request"))
            : await EnsureCommunityMemberAsync(request.CommunityId, actorUserId, dbContext, request);
    }


    public async Task<RepositoryResult<PagedResult<Features.Offers.Models.Offer>>> GetOffersAsync(
        string id,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Features.Offers.Models.Offer>>.Failure(ApiErrors.Unauthorized());
        }

        var request = await dbContext.Requests.FindAsync(id);
        if (request is null)
        {
            return RepositoryResult<PagedResult<Features.Offers.Models.Offer>>.Failure(
                ApiErrors.NotFound("Request"));
        }

        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == request.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<PagedResult<Features.Offers.Models.Offer>>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        var pageNumber = page.GetValueOrDefault(1);
        var size = pageSize.GetValueOrDefault(20);
        if (pageNumber <= 0 || size <= 0 || size > 100)
        {
            return RepositoryResult<PagedResult<Features.Offers.Models.Offer>>.Failure(
                ApiErrors.Invalid("Invalid pagination parameters."));
        }

        var query = dbContext.Offers
            .Include(offer => offer.Community)
            .Include(offer => offer.OffererUser)
            .Where(offer => offer.RequestId == id);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(offer => offer.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync();

        return RepositoryResult<PagedResult<Features.Offers.Models.Offer>>.Success(
            new PagedResult<Features.Offers.Models.Offer>(items, pageNumber, size, total));
    }


    public async Task<RepositoryResult<PagedResult<Request>>> GetMineAsync(
        string? communityId,
        string? status,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Request>>.Failure(ApiErrors.Unauthorized());
        }

        var pageNumber = page.GetValueOrDefault(1);
        var size = pageSize.GetValueOrDefault(20);
        if (pageNumber <= 0 || size <= 0 || size > 100)
        {
            return RepositoryResult<PagedResult<Request>>.Failure(
                ApiErrors.Invalid("Invalid pagination parameters."));
        }

        var query = dbContext.Requests
            .Include(request => request.Community)
            .Include(request => request.RequesterUser)
            .AsQueryable()
            .Where(request => request.RequesterUserId == actorUserId);

        if (!string.IsNullOrWhiteSpace(communityId))
        {
            query = query.Where(request => request.CommunityId == communityId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RequestStatus>(status, true, out var requestStatus))
            {
                return RepositoryResult<PagedResult<Request>>.Failure(
                    ApiErrors.Invalid("Invalid status filter."));
            }
            query = query.Where(request => request.Status == requestStatus);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(request => request.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync();

        return RepositoryResult<PagedResult<Request>>.Success(
            new PagedResult<Request>(items, pageNumber, size, total));
    }


    public async Task<RepositoryResult<Request>> CreateAsync(
        Request body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.RequesterUserId))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Required(nameof(body.RequesterUserId)));
        }
        if (!string.Equals(body.RequesterUserId, actorUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Invalid("RequesterUserId must match the current user."));
        }
        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Required(nameof(body.Title)));
        }
        if (body.Status != RequestStatus.Open)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("Status must be Open on create."));
        }
        var dayStart = DateTime.UtcNow.Date;
        var dailyCount = await dbContext.Requests.CountAsync(request =>
            request.CommunityId == body.CommunityId
            && request.RequesterUserId == body.RequesterUserId
            && request.CreatedAt >= dayStart);
        if (dailyCount >= MaxDailyRequestsPerUser)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("Daily request limit reached."));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var requesterExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.RequesterUserId);
        if (!requesterExists)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("RequesterUserId does not exist."));
        }
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.RequesterUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Invalid("RequesterUserId is not a member of the community."));
        }
        var normalizedTitle = NormalizeText(body.Title);
        var normalizedDescription = NormalizeText(body.Description);
        var duplicateSince = DateTime.UtcNow.Subtract(DuplicateWindow);
        var hasDuplicate = await dbContext.Requests.AnyAsync(request =>
            request.CommunityId == body.CommunityId
            && request.RequesterUserId == body.RequesterUserId
            && request.CreatedAt >= duplicateSince
            && request.Title.Trim().ToLower() == normalizedTitle
            && request.Description.Trim().ToLower() == normalizedDescription);
        if (hasDuplicate)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("Duplicate request detected."));
        }
        if (string.IsNullOrWhiteSpace(body.Id))
        {
            body.Id = Guid.NewGuid().ToString();
        }
        body.CreatedAt = DateTime.UtcNow;

        dbContext.Requests.Add(body);
        await dbContext.SaveChangesAsync();
        var createdRequest = await dbContext.Requests
            .Include(request => request.Community)
            .Include(request => request.RequesterUser)
            .FirstOrDefaultAsync(request => request.Id == body.Id);

        return RepositoryResult<Request>.Success(createdRequest ?? body);
    }


    public async Task<RepositoryResult<Request>> UpdateAsync(
        string id,
        Request body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Unauthorized());
        }
        var request = await dbContext.Requests.FindAsync(id);
        if (request is null)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.NotFound("Request"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == request.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Invalid("User is not allowed to update the request."));
        }
        if (request.Status != RequestStatus.Open)
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Invalid("Request cannot be updated unless open."));
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.RequesterUserId))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Required(nameof(body.RequesterUserId)));
        }
        if (!CanManageCommunity(membership)
            && !string.Equals(body.RequesterUserId, request.RequesterUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("RequesterUserId cannot be changed."));
        }
        if (string.IsNullOrWhiteSpace(body.Title))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Required(nameof(body.Title)));
        }
        if (body.Status != RequestStatus.Open)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("Status cannot be changed via update."));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var requesterExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.RequesterUserId);
        if (!requesterExists)
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("RequesterUserId does not exist."));
        }
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.RequesterUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Invalid("RequesterUserId is not a member of the community."));
        }

        request.CommunityId = body.CommunityId;
        request.RequesterUserId = body.RequesterUserId;
        request.Title = body.Title;
        request.Description = body.Description;
        request.Status = body.Status;
        request.NeededFrom = body.NeededFrom;
        request.NeededTo = body.NeededTo;

        await dbContext.SaveChangesAsync();
        var updatedRequest = await dbContext.Requests
            .Include(foundRequest => foundRequest.Community)
            .Include(foundRequest => foundRequest.RequesterUser)
            .FirstOrDefaultAsync(foundRequest => foundRequest.Id == request.Id);

        return RepositoryResult<Request>.Success(updatedRequest ?? request);
    }


    public async Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = _currentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        var request = await dbContext.Requests.FindAsync(id);
        if (request is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Request"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == request.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not allowed to delete the request."));
        }
        if (request.Status != RequestStatus.Open)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("Request cannot be deleted unless open."));
        }

        dbContext.Requests.Remove(request);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    private static string NormalizeText(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static async Task<RepositoryResult<Request>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        CondivaDbContext dbContext,
        Request request)
    {
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        return RepositoryResult<Request>.Success(request);
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }
}
