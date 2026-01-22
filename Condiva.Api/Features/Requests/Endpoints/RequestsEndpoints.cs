using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Offers.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using RequestModel = Condiva.Api.Features.Requests.Models.Request;
using RequestStatus = Condiva.Api.Features.Requests.Models.RequestStatus;

namespace Condiva.Api.Features.Requests.Endpoints;

public static class RequestsEndpoints
{
    private const int MaxDailyRequestsPerUser = 3;
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(8);

    public static IEndpointRouteBuilder MapRequestsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/requests");
        group.RequireAuthorization();
        group.WithTags("Requests");

        group.MapGet("/", async (CondivaDbContext dbContext) =>
            await dbContext.Requests.ToListAsync());

        group.MapGet("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var request = await dbContext.Requests.FindAsync(id);
            return request is null ? ApiErrors.NotFound("Request") : Results.Ok(request);
        });

        group.MapGet("/{id}/offers", async (
            string id,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var request = await dbContext.Requests.FindAsync(id);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }

            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == request.CommunityId && membership.UserId == actorUserId);
            if (!isMember)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            var pageNumber = page.GetValueOrDefault(1);
            var size = pageSize.GetValueOrDefault(20);
            if (pageNumber <= 0 || size <= 0 || size > 100)
            {
                return ApiErrors.Invalid("Invalid pagination parameters.");
            }

            var query = dbContext.Offers
                .Where(offer => offer.RequestId == id);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(offer => offer.CreatedAt)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();

            return Results.Ok(new
            {
                items,
                page = pageNumber,
                pageSize = size,
                total
            });
        });

        group.MapGet("/me", async (
            string? communityId,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var pageNumber = page.GetValueOrDefault(1);
            var size = pageSize.GetValueOrDefault(20);
            if (pageNumber <= 0 || size <= 0 || size > 100)
            {
                return ApiErrors.Invalid("Invalid pagination parameters.");
            }

            var query = dbContext.Requests.AsQueryable()
                .Where(request => request.RequesterUserId == actorUserId);

            if (!string.IsNullOrWhiteSpace(communityId))
            {
                query = query.Where(request => request.CommunityId == communityId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<RequestStatus>(status, true, out var requestStatus))
                {
                    return ApiErrors.Invalid("Invalid status filter.");
                }
                query = query.Where(request => request.Status == requestStatus);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(request => request.CreatedAt)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();

            return Results.Ok(new
            {
                items,
                page = pageNumber,
                pageSize = size,
                total
            });
        });

        group.MapPost("/", async (
            RequestModel body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(body.RequesterUserId))
            {
                return ApiErrors.Required(nameof(body.RequesterUserId));
            }
            if (!string.Equals(body.RequesterUserId, actorUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("RequesterUserId must match the current user.");
            }
            if (string.IsNullOrWhiteSpace(body.Title))
            {
                return ApiErrors.Required(nameof(body.Title));
            }
            if (body.Status != Models.RequestStatus.Open)
            {
                return ApiErrors.Invalid("Status must be Open on create.");
            }
            var dayStart = DateTime.UtcNow.Date;
            var dailyCount = await dbContext.Requests.CountAsync(request =>
                request.CommunityId == body.CommunityId
                && request.RequesterUserId == body.RequesterUserId
                && request.CreatedAt >= dayStart);
            if (dailyCount >= MaxDailyRequestsPerUser)
            {
                return ApiErrors.Invalid("Daily request limit reached.");
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var requesterExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.RequesterUserId);
            if (!requesterExists)
            {
                return ApiErrors.Invalid("RequesterUserId does not exist.");
            }
            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.RequesterUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("RequesterUserId is not a member of the community.");
            }
            var normalizedTitle = NormalizeText(body.Title);
            var normalizedDescription = NormalizeText(body.Description);
            var duplicateSince = DateTime.UtcNow.Subtract(DuplicateWindow);
            var hasDuplicate = await dbContext.Requests.AnyAsync(request =>
                request.CommunityId == body.CommunityId
                && request.RequesterUserId == body.RequesterUserId
                && request.CreatedAt >= duplicateSince
                && NormalizeText(request.Title) == normalizedTitle
                && NormalizeText(request.Description) == normalizedDescription);
            if (hasDuplicate)
            {
                return ApiErrors.Invalid("Duplicate request detected.");
            }
            if (string.IsNullOrWhiteSpace(body.Id))
            {
                body.Id = Guid.NewGuid().ToString();
            }
            body.CreatedAt = DateTime.UtcNow;

            dbContext.Requests.Add(body);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/requests/{body.Id}", body);
        });

        group.MapPut("/{id}", async (
            string id,
            RequestModel body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var request = await dbContext.Requests.FindAsync(id);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == request.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to update the request.");
            }
            if (request.Status != RequestStatus.Open)
            {
                return ApiErrors.Invalid("Request cannot be updated unless open.");
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(body.RequesterUserId))
            {
                return ApiErrors.Required(nameof(body.RequesterUserId));
            }
            if (!CanManageCommunity(membership)
                && !string.Equals(body.RequesterUserId, request.RequesterUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("RequesterUserId cannot be changed.");
            }
            if (string.IsNullOrWhiteSpace(body.Title))
            {
                return ApiErrors.Required(nameof(body.Title));
            }
            if (body.Status != RequestStatus.Open)
            {
                return ApiErrors.Invalid("Status cannot be changed via update.");
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var requesterExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.RequesterUserId);
            if (!requesterExists)
            {
                return ApiErrors.Invalid("RequesterUserId does not exist.");
            }
            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.RequesterUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("RequesterUserId is not a member of the community.");
            }

            request.CommunityId = body.CommunityId;
            request.RequesterUserId = body.RequesterUserId;
            request.Title = body.Title;
            request.Description = body.Description;
            request.Status = body.Status;
            request.NeededFrom = body.NeededFrom;
            request.NeededTo = body.NeededTo;

            await dbContext.SaveChangesAsync();
            return Results.Ok(request);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var request = await dbContext.Requests.FindAsync(id);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == request.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to delete the request.");
            }
            if (request.Status != RequestStatus.Open)
            {
                return ApiErrors.Invalid("Request cannot be deleted unless open.");
            }

            dbContext.Requests.Remove(request);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        return endpoints;
    }

    private static string NormalizeText(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }
}
