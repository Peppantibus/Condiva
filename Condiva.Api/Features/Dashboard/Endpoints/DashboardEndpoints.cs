using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Dashboard.Dtos;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Requests.Dtos;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Dashboard.Endpoints;

public static class DashboardEndpoints
{
    private const int DefaultPreviewSize = 5;
    private const int MaxPreviewSize = 20;
    private const int MaxCommunityIdLength = 64;
    private const int ThumbnailPresignTtlSeconds = 300;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard");
        group.RequireAuthorization();
        group.WithTags("Dashboard");

        group.MapGet("/{communityId}", async (
            string communityId,
            int? previewSize,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IMapper mapper,
            IR2StorageService storageService) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var normalizedCommunityId = Normalize(communityId);
            var communityIdError = ValidateCommunityId(normalizedCommunityId);
            if (communityIdError is not null)
            {
                return communityIdError;
            }
            var communityIdValue = normalizedCommunityId!;

            var actorMembership = await dbContext.Memberships.AsNoTracking().FirstOrDefaultAsync(membership =>
                membership.CommunityId == communityIdValue
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (actorMembership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var size = ClampPreviewSize(previewSize);
            var openRequestsQuery = dbContext.Requests
                .AsNoTracking()
                .Include(request => request.Community)
                .Include(request => request.RequesterUser)
                .Where(request =>
                    request.CommunityId == communityIdValue
                    && request.Status == RequestStatus.Open);
            var availableItemsQuery = dbContext.Items
                .AsNoTracking()
                .Include(item => item.OwnerUser)
                .Where(item =>
                    item.CommunityId == communityIdValue
                    && item.Status == ItemStatus.Available);
            var myRequestsQuery = dbContext.Requests
                .AsNoTracking()
                .Include(request => request.Community)
                .Include(request => request.RequesterUser)
                .Where(request =>
                    request.CommunityId == communityIdValue
                    && request.RequesterUserId == actorUserId);

            var openRequestsTotal = await openRequestsQuery.CountAsync();
            var availableItemsTotal = await availableItemsQuery.CountAsync();
            var myRequestsTotal = await myRequestsQuery.CountAsync();

            var openRequests = await openRequestsQuery
                .OrderByDescending(request => request.CreatedAt)
                .Take(size)
                .ToListAsync();
            var availableItems = await availableItemsQuery
                .OrderByDescending(item => item.CreatedAt)
                .Take(size)
                .ToListAsync();
            var myRequests = await myRequestsQuery
                .OrderByDescending(request => request.CreatedAt)
                .Take(size)
                .ToListAsync();

            var openRequestsPreview = openRequests
                .Select(request => mapper.Map<Request, RequestListItemDto>(request) with
                {
                    AllowedActions = AllowedActionsPolicy.ForRequest(request, actorUserId, actorMembership.Role)
                })
                .Select(request => new DashboardPreviewItemDto(
                    request.Id,
                    request.Title,
                    request.Status,
                    request.Owner,
                    request.CreatedAt,
                    ResolveThumbnailUrl(request.ImageKey, storageService),
                    request.AllowedActions))
                .ToList();
            var availableItemsPreview = availableItems
                .Select(item => mapper.Map<Item, ItemListItemDto>(item) with
                {
                    AllowedActions = AllowedActionsPolicy.ForItem(item, actorUserId, actorMembership.Role)
                })
                .Select(item => new DashboardPreviewItemDto(
                    item.Id,
                    item.Name,
                    item.Status,
                    item.Owner,
                    item.CreatedAt,
                    ResolveThumbnailUrl(item.ImageKey, storageService),
                    item.AllowedActions))
                .ToList();
            var myRequestsPreview = myRequests
                .Select(request => mapper.Map<Request, RequestListItemDto>(request) with
                {
                    AllowedActions = AllowedActionsPolicy.ForRequest(request, actorUserId, actorMembership.Role)
                })
                .Select(request => new DashboardPreviewItemDto(
                    request.Id,
                    request.Title,
                    request.Status,
                    request.Owner,
                    request.CreatedAt,
                    ResolveThumbnailUrl(request.ImageKey, storageService),
                    request.AllowedActions))
                .ToList();

            var payload = new DashboardSummaryDto(
                openRequestsPreview,
                availableItemsPreview,
                myRequestsPreview,
                new DashboardCountersDto(openRequestsTotal, availableItemsTotal, myRequestsTotal));
            return Results.Ok(payload);
        })
            .Produces<DashboardSummaryDto>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static int ClampPreviewSize(int? value)
    {
        var size = value.GetValueOrDefault(DefaultPreviewSize);
        if (size < 1)
        {
            return 1;
        }

        return size > MaxPreviewSize ? MaxPreviewSize : size;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IResult? ValidateCommunityId(string? communityId)
    {
        if (string.IsNullOrWhiteSpace(communityId))
        {
            return ApiErrors.Required("communityId");
        }

        return communityId.Length > MaxCommunityIdLength
            ? ApiErrors.Invalid("Invalid communityId.")
            : null;
    }

    private static string? ResolveThumbnailUrl(string? imageKey, IR2StorageService storageService)
    {
        return string.IsNullOrWhiteSpace(imageKey)
            ? null
            : storageService.GeneratePresignedGetUrl(imageKey, ThumbnailPresignTtlSeconds);
    }
}
