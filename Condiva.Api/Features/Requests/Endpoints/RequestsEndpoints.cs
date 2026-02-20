using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Concurrency;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Offers.Dtos;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Data;
using Condiva.Api.Features.Requests.Dtos;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Features.Storage.Dtos;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Requests.Endpoints;

public static class RequestsEndpoints
{
    private const int DownloadPresignTtlSeconds = 300;

    public static IEndpointRouteBuilder MapRequestsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/requests");
        group.RequireAuthorization();
        group.WithTags("Requests");

        group.MapGet("/", async (
            string? communityId,
            string? status,
            string? cursor,
            int? pageSize,
            string? sort,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(communityId))
            {
                return ApiErrors.Required(nameof(communityId));
            }
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetListAsync(
                communityId,
                status,
                cursor,
                pageSize,
                sort,
                user,
                dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRole = await ActorMembershipRoles.GetRoleAsync(dbContext, actorUserId, communityId);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var mapped = result.Data!
                .Items
                .Select(request => mapper.Map<Request, RequestListItemDto>(request) with
                {
                    AllowedActions = AllowedActionsPolicy.ForRequest(request, actorUserId, actorRole.Value)
                })
                .ToList();
            var (sortField, sortOrder) = ParseSort(sort);
            var payload = new PagedResponseDto<RequestListItemDto>(
                mapped,
                1,
                result.Data.PageSize,
                result.Data.Total,
                sortField,
                sortOrder,
                result.Data.Cursor,
                result.Data.NextCursor);
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<RequestListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            HttpContext httpContext,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetByIdAsync(id, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRole = await ActorMembershipRoles.GetRoleAsync(
                dbContext,
                actorUserId,
                result.Data!.CommunityId);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var payload = mapper.Map<Request, RequestDetailsDto>(result.Data!) with
            {
                AllowedActions = AllowedActionsPolicy.ForRequest(result.Data!, actorUserId, actorRole.Value)
            };
            EntityTagHelper.Set(httpContext, result.Data!);
            return Results.Ok(payload);
        })
            .Produces<RequestDetailsDto>(StatusCodes.Status200OK);

        group.MapGet("/{id}/offers", async (
            string id,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetOffersAsync(id, page, pageSize, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var request = await dbContext.Requests.FirstOrDefaultAsync(foundRequest => foundRequest.Id == id);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }

            var actorRole = await ActorMembershipRoles.GetRoleAsync(
                dbContext,
                actorUserId,
                request.CommunityId);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var isRequestOwner = string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal);
            var mapped = result.Data!.Items
                .Select(offer => mapper.Map<Offer, OfferListItemDto>(offer) with
                {
                    AllowedActions = AllowedActionsPolicy.ForOffer(
                        offer,
                        actorUserId,
                        actorRole.Value,
                        isRequestOwner)
                })
                .ToList();
            var payload = new PagedResponseDto<OfferListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total,
                "createdAt",
                "desc");
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<OfferListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/me", async (
            string? communityId,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetMineAsync(communityId, status, page, pageSize, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRolesByCommunity = await ActorMembershipRoles.GetRolesByCommunityAsync(dbContext, actorUserId);
            var mapped = result.Data!.Items
                .Select(request =>
                {
                    if (!actorRolesByCommunity.TryGetValue(request.CommunityId, out var actorRole))
                    {
                        return mapper.Map<Request, RequestListItemDto>(request);
                    }

                    return mapper.Map<Request, RequestListItemDto>(request) with
                    {
                        AllowedActions = AllowedActionsPolicy.ForRequest(request, actorUserId, actorRole)
                    };
                })
                .ToList();
            var payload = new PagedResponseDto<RequestListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total,
                "createdAt",
                "desc");
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<RequestListItemDto>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateRequestRequestDto body,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            HttpContext httpContext,
            CondivaDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<RequestStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var model = new Request
            {
                CommunityId = body.CommunityId,
                RequesterUserId = actorUserId,
                Title = body.Title,
                Description = body.Description,
                Status = statusValue,
                NeededFrom = body.NeededFrom,
                NeededTo = body.NeededTo
            };

            var result = await repository.CreateAsync(model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Request, RequestDetailsDto>(result.Data!);
            EntityTagHelper.Set(httpContext, result.Data!);
            return Results.Created($"/api/requests/{payload.Id}", payload);
        })
            .Produces<RequestDetailsDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", async (
            string id,
            UpdateRequestRequestDto body,
            ClaimsPrincipal user,
            HttpRequest request,
            IRequestRepository repository,
            IMapper mapper,
            HttpContext httpContext,
            CondivaDbContext dbContext) =>
        {
            var currentResult = await repository.GetByIdAsync(id, user, dbContext);
            if (!currentResult.IsSuccess)
            {
                return currentResult.Error!;
            }
            if (!EntityTagHelper.IsIfMatchSatisfied(request.Headers.IfMatch.ToString(), currentResult.Data!))
            {
                return ApiErrors.PreconditionFailed("Resource version mismatch. Refresh and retry.");
            }

            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<RequestStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Request
            {
                CommunityId = body.CommunityId,
                RequesterUserId = body.RequesterUserId,
                Title = body.Title,
                Description = body.Description,
                Status = statusValue,
                NeededFrom = body.NeededFrom,
                NeededTo = body.NeededTo
            };

            var result = await repository.UpdateAsync(id, model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Request, RequestDetailsDto>(result.Data!);
            EntityTagHelper.Set(httpContext, result.Data!);
            return Results.Ok(payload);
        })
            .Produces<RequestDetailsDto>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            HttpRequest request,
            IRequestRepository repository,
            CondivaDbContext dbContext) =>
        {
            var currentResult = await repository.GetByIdAsync(id, user, dbContext);
            if (!currentResult.IsSuccess)
            {
                return currentResult.Error!;
            }
            if (!EntityTagHelper.IsIfMatchSatisfied(request.Headers.IfMatch.ToString(), currentResult.Data!))
            {
                return ApiErrors.PreconditionFailed("Resource version mismatch. Refresh and retry.");
            }

            var result = await repository.DeleteAsync(id, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{id}/image/presign", async (
            string id,
            StoragePresignRequestDto body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
        {
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var request = await dbContext.Requests.FirstOrDefaultAsync(foundRequest => foundRequest.Id == id);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(foundMembership =>
                foundMembership.CommunityId == request.CommunityId
                && foundMembership.UserId == actorUserId
                && foundMembership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            if (!CanManageRequest(membership, request.RequesterUserId, actorUserId))
            {
                return ApiErrors.Forbidden("User is not allowed to manage the request image.");
            }

            if (request.Status != RequestStatus.Open)
            {
                return ApiErrors.Invalid("Request image can be managed only while request is open.");
            }

            if (string.IsNullOrWhiteSpace(body.FileName))
            {
                return ApiErrors.Required(nameof(body.FileName));
            }

            if (!StorageImageKeyHelper.IsAllowedImageContentType(body.ContentType))
            {
                return ApiErrors.Invalid("Unsupported contentType.");
            }

            var scope = $"requests/{request.Id}/image";
            var objectKey = StorageImageKeyHelper.BuildScopedObjectKey(
                scope,
                StorageImageKeyHelper.SanitizeFileName(body.FileName));
            var uploadUrl = storageService.GeneratePresignedPutUrl(
                objectKey,
                body.ContentType.Trim(),
                storageService.DefaultPresignTtlSeconds);

            return Results.Ok(new StoragePresignResponseDto(
                objectKey,
                uploadUrl,
                storageService.DefaultPresignTtlSeconds));
        })
            .Produces<StoragePresignResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/image/confirm", async (
            string id,
            StorageConfirmRequestDto body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            if (!StorageImageKeyHelper.TryNormalizeObjectKey(body.ObjectKey, out var objectKey))
            {
                return ApiErrors.Invalid("Invalid objectKey.");
            }

            var request = await dbContext.Requests.FirstOrDefaultAsync(foundRequest => foundRequest.Id == id, cancellationToken);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }

            var scope = $"requests/{request.Id}/image";
            if (!StorageImageKeyHelper.IsScopedKey(objectKey, scope))
            {
                return ApiErrors.Invalid("objectKey is outside allowed scope.");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(foundMembership =>
                foundMembership.CommunityId == request.CommunityId
                && foundMembership.UserId == actorUserId
                && foundMembership.Status == MembershipStatus.Active,
                cancellationToken);
            if (membership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            if (!CanManageRequest(membership, request.RequesterUserId, actorUserId))
            {
                return ApiErrors.Forbidden("User is not allowed to manage the request image.");
            }

            if (request.Status != RequestStatus.Open)
            {
                return ApiErrors.Invalid("Request image can be managed only while request is open.");
            }

            var previousObjectKey = request.ImageKey;
            request.ImageKey = objectKey;
            await dbContext.SaveChangesAsync(cancellationToken);

            await TryDeletePreviousObjectAsync(
                previousObjectKey,
                objectKey,
                storageService,
                loggerFactory.CreateLogger("Requests.Image"),
                cancellationToken);

            return Results.Ok(new { objectKey });
        })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/image", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
        {
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var request = await dbContext.Requests.FirstOrDefaultAsync(foundRequest => foundRequest.Id == id);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }

            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == request.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            if (string.IsNullOrWhiteSpace(request.ImageKey))
            {
                return ApiErrors.NotFound("RequestImage");
            }

            var downloadUrl = storageService.GeneratePresignedGetUrl(request.ImageKey, DownloadPresignTtlSeconds);
            return Results.Ok(new RequestImageResponseDto(request.ImageKey, downloadUrl));
        })
            .Produces<RequestImageResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}/image", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var request = await dbContext.Requests.FirstOrDefaultAsync(foundRequest => foundRequest.Id == id, cancellationToken);
            if (request is null)
            {
                return ApiErrors.NotFound("Request");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(foundMembership =>
                foundMembership.CommunityId == request.CommunityId
                && foundMembership.UserId == actorUserId
                && foundMembership.Status == MembershipStatus.Active,
                cancellationToken);
            if (membership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            if (!CanManageRequest(membership, request.RequesterUserId, actorUserId))
            {
                return ApiErrors.Forbidden("User is not allowed to manage the request image.");
            }

            if (request.Status != RequestStatus.Open)
            {
                return ApiErrors.Invalid("Request image can be managed only while request is open.");
            }

            var previousObjectKey = request.ImageKey;
            if (string.IsNullOrWhiteSpace(previousObjectKey))
            {
                return Results.NoContent();
            }

            request.ImageKey = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            var logger = loggerFactory.CreateLogger("Requests.Image");
            try
            {
                await storageService.DeleteObjectAsync(previousObjectKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed deleting request image from R2. key: {ObjectKey}", previousObjectKey);
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static bool CanManageRequest(Membership membership, string requesterUserId, string actorUserId)
    {
        return MembershipRolePolicy.CanModerateContent(membership.Role)
            || string.Equals(requesterUserId, actorUserId, StringComparison.Ordinal);
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    private static async Task TryDeletePreviousObjectAsync(
        string? previousObjectKey,
        string currentObjectKey,
        IR2StorageService storageService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previousObjectKey)
            || string.Equals(previousObjectKey, currentObjectKey, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await storageService.DeleteObjectAsync(previousObjectKey, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed deleting previous request image from R2. key: {ObjectKey}", previousObjectKey);
        }
    }

    private static (string Sort, string Order) ParseSort(string? sort)
    {
        var normalized = string.IsNullOrWhiteSpace(sort)
            ? "createdat:desc"
            : sort.Trim().ToLowerInvariant();

        return normalized switch
        {
            "createdat:asc" => ("createdAt", "asc"),
            _ => ("createdAt", "desc")
        };
    }

    public sealed record RequestImageResponseDto(string ObjectKey, string DownloadUrl);
}
