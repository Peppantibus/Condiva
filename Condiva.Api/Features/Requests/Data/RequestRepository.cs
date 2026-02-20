using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using System.Security.Claims;

namespace Condiva.Api.Features.Requests.Data;

public sealed class RequestRepository : IRequestRepository
{
    private const int MaxDailyRequestsPerUser = 3;
    private const int DefaultCursorPageSize = 20;
    private const int MaxCursorPageSize = 100;
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(8);

    public async Task<RepositoryResult<CursorPagedResult<Request>>> GetListAsync(
        string communityId,
        string? status,
        string? cursor,
        int? pageSize,
        string? sort,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<CursorPagedResult<Request>>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return RepositoryResult<CursorPagedResult<Request>>.Failure(ApiErrors.Required(nameof(communityId)));
        }

        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active
            && membership.CommunityId == communityId);
        if (!isMember)
        {
            return RepositoryResult<CursorPagedResult<Request>>.Failure(
                ApiErrors.Forbidden("User is not a member of the community."));
        }

        var size = pageSize.GetValueOrDefault(DefaultCursorPageSize);
        if (size <= 0 || size > MaxCursorPageSize)
        {
            return RepositoryResult<CursorPagedResult<Request>>.Failure(
                ApiErrors.Invalid("Invalid pageSize."));
        }

        if (!TryParseSort(sort, out var isDescending))
        {
            return RepositoryResult<CursorPagedResult<Request>>.Failure(
                ApiErrors.Invalid("Invalid sort. Use createdAt:desc or createdAt:asc."));
        }

        RequestStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RequestStatus>(status.Trim(), true, out var parsedStatus))
            {
                return RepositoryResult<CursorPagedResult<Request>>.Failure(
                    ApiErrors.Invalid("Invalid status filter."));
            }

            statusFilter = parsedStatus;
        }

        CursorToken? cursorToken = null;
        var normalizedCursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedCursor))
        {
            if (!TryParseCursor(normalizedCursor, out var parsedToken))
            {
                return RepositoryResult<CursorPagedResult<Request>>.Failure(
                    ApiErrors.Invalid("Invalid cursor."));
            }

            cursorToken = parsedToken;
        }

        var baseQuery = dbContext.Requests
            .AsNoTracking()
            .Include(request => request.Community)
            .Include(request => request.RequesterUser)
            .Where(request => request.CommunityId == communityId);

        if (statusFilter.HasValue)
        {
            baseQuery = baseQuery.Where(request => request.Status == statusFilter.Value);
        }

        var pageQuery = baseQuery;
        if (cursorToken is not null)
        {
            if (isDescending)
            {
                pageQuery = pageQuery.Where(request =>
                    request.CreatedAt < cursorToken.Value.CreatedAt
                    || (request.CreatedAt == cursorToken.Value.CreatedAt
                        && string.Compare(request.Id, cursorToken.Value.Id) < 0));
            }
            else
            {
                pageQuery = pageQuery.Where(request =>
                    request.CreatedAt > cursorToken.Value.CreatedAt
                    || (request.CreatedAt == cursorToken.Value.CreatedAt
                        && string.Compare(request.Id, cursorToken.Value.Id) > 0));
            }
        }

        pageQuery = isDescending
            ? pageQuery.OrderByDescending(request => request.CreatedAt).ThenByDescending(request => request.Id)
            : pageQuery.OrderBy(request => request.CreatedAt).ThenBy(request => request.Id);

        var total = await baseQuery.CountAsync();
        var pageItems = await pageQuery
            .Take(size + 1)
            .ToListAsync();

        var hasMore = pageItems.Count > size;
        var items = hasMore ? pageItems.Take(size).ToList() : pageItems;
        var nextCursor = hasMore
            ? CreateCursor(items[^1].CreatedAt, items[^1].Id)
            : null;

        return RepositoryResult<CursorPagedResult<Request>>.Success(
            new CursorPagedResult<Request>(items, size, total, normalizedCursor, nextCursor));
    }

    public async Task<RepositoryResult<Request>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
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
        var actorUserId = CurrentUser.GetUserId(user);
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
            .Include(offer => offer.Item)
            .ThenInclude(item => item!.OwnerUser)
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
        var actorUserId = CurrentUser.GetUserId(user);
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
        var actorUserId = CurrentUser.GetUserId(user);
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
            body.RequesterUserId = actorUserId;
        }
        else if (!string.Equals(body.RequesterUserId, actorUserId, StringComparison.Ordinal))
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
                ApiErrors.Forbidden("User is not a member of the community."));
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
        var actorUserId = CurrentUser.GetUserId(user);
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
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Request>.Failure(
                ApiErrors.Forbidden("User is not allowed to update the request."));
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
        if (!string.IsNullOrWhiteSpace(body.RequesterUserId)
            && !string.Equals(body.RequesterUserId, request.RequesterUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Request>.Failure(ApiErrors.Invalid("RequesterUserId cannot be changed."));
        }
        body.RequesterUserId = request.RequesterUserId;
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
        var actorUserId = CurrentUser.GetUserId(user);
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
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Forbidden("User is not allowed to delete the request."));
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

    private static bool TryParseSort(string? sort, out bool isDescending)
    {
        var normalized = string.IsNullOrWhiteSpace(sort)
            ? "createdat:desc"
            : sort.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "createdat:desc":
                isDescending = true;
                return true;
            case "createdat:asc":
                isDescending = false;
                return true;
            default:
                isDescending = true;
                return false;
        }
    }

    private static string CreateCursor(DateTime createdAt, string id)
    {
        var payload = $"{NormalizeCursorDate(createdAt)}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryParseCursor(string cursor, out CursorToken token)
    {
        token = default;

        byte[] data;
        try
        {
            data = Convert.FromBase64String(cursor);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetString(data);
        var parts = payload.Split('|', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        token = new CursorToken(createdAt, parts[1]);
        return true;
    }

    private static string NormalizeCursorDate(DateTime value)
    {
        var normalized = value.Kind == DateTimeKind.Utc
            ? value
            : value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();

        return normalized.ToString("O", CultureInfo.InvariantCulture);
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
        return MembershipRolePolicy.CanModerateContent(membership.Role);
    }

    private readonly record struct CursorToken(DateTime CreatedAt, string Id);
}
