using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Concurrency;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Items.Data;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Storage.Dtos;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Items.Endpoints;

public static class ItemsEndpoints
{
    private const int DownloadPresignTtlSeconds = 300;

    public static IEndpointRouteBuilder MapItemsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/items");
        group.RequireAuthorization();
        group.WithTags("Items");

        group.MapGet("/", async (
            string? communityId,
            string? owner,
            string? status,
            string? category,
            string? search,
            string? sort,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(communityId))
            {
                return ApiErrors.Required(nameof(communityId));
            }
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetAllAsync(
                communityId,
                owner,
                status,
                category,
                search,
                sort,
                page,
                pageSize,
                user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRole = await ActorMembershipRoles.GetRoleAsync(dbContext, actorUserId, communityId);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var mapped = result.Data!.Items
                .Select(item => mapper.Map<Item, ItemListItemDto>(item) with
                {
                    AllowedActions = AllowedActionsPolicy.ForItem(item, actorUserId, actorRole.Value)
                })
                .ToList();
            var (sortField, sortOrder) = ParseSort(sort);

            var payload = new PagedResponseDto<ItemListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total,
                sortField,
                sortOrder);
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<ItemListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper,
            HttpContext httpContext,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetByIdAsync(id, user);
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

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!) with
            {
                AllowedActions = AllowedActionsPolicy.ForItem(result.Data!, actorUserId, actorRole.Value)
            };
            EntityTagHelper.Set(httpContext, result.Data!);
            return Results.Ok(payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateItemRequestDto body,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper,
            HttpContext httpContext) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<ItemStatus>(body.Status, true, out var status))
            {
                return ApiErrors.Invalid("Invalid status.");
            }
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var model = new Item
            {
                CommunityId = body.CommunityId,
                OwnerUserId = actorUserId,
                Name = body.Name,
                Description = body.Description,
                Category = body.Category,
                Status = status
            };

            var result = await repository.CreateAsync(model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            EntityTagHelper.Set(httpContext, result.Data!);
            return Results.Created($"/api/items/{payload.Id}", payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", async (
            string id,
            UpdateItemRequestDto body,
            ClaimsPrincipal user,
            HttpRequest request,
            IItemRepository repository,
            IMapper mapper,
            HttpContext httpContext) =>
        {
            var currentResult = await repository.GetByIdAsync(id, user);
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
            if (!Enum.TryParse<ItemStatus>(body.Status, true, out var status))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Item
            {
                CommunityId = body.CommunityId,
                OwnerUserId = body.OwnerUserId,
                Name = body.Name,
                Description = body.Description,
                Category = body.Category,
                Status = status
            };

            var result = await repository.UpdateAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            EntityTagHelper.Set(httpContext, result.Data!);
            return Results.Ok(payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            HttpRequest request,
            IItemRepository repository) =>
        {
            var currentResult = await repository.GetByIdAsync(id, user);
            if (!currentResult.IsSuccess)
            {
                return currentResult.Error!;
            }
            if (!EntityTagHelper.IsIfMatchSatisfied(request.Headers.IfMatch.ToString(), currentResult.Data!))
            {
                return ApiErrors.PreconditionFailed("Resource version mismatch. Refresh and retry.");
            }

            var result = await repository.DeleteAsync(id, user);
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

            var item = await dbContext.Items.FirstOrDefaultAsync(foundItem => foundItem.Id == id);
            if (item is null)
            {
                return ApiErrors.NotFound("Item");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(foundMembership =>
                foundMembership.CommunityId == item.CommunityId
                && foundMembership.UserId == actorUserId
                && foundMembership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            if (!CanManageItem(membership, item.OwnerUserId, actorUserId))
            {
                return ApiErrors.Forbidden("User is not allowed to manage the item image.");
            }

            if (string.IsNullOrWhiteSpace(body.FileName))
            {
                return ApiErrors.Required(nameof(body.FileName));
            }

            if (!StorageImageKeyHelper.IsAllowedImageContentType(body.ContentType))
            {
                return ApiErrors.Invalid("Unsupported contentType.");
            }

            var scope = $"items/{item.Id}/image";
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

            var item = await dbContext.Items.FirstOrDefaultAsync(foundItem => foundItem.Id == id, cancellationToken);
            if (item is null)
            {
                return ApiErrors.NotFound("Item");
            }

            var scope = $"items/{item.Id}/image";
            if (!StorageImageKeyHelper.IsScopedKey(objectKey, scope))
            {
                return ApiErrors.Invalid("objectKey is outside allowed scope.");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(foundMembership =>
                foundMembership.CommunityId == item.CommunityId
                && foundMembership.UserId == actorUserId
                && foundMembership.Status == MembershipStatus.Active,
                cancellationToken);
            if (membership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            if (!CanManageItem(membership, item.OwnerUserId, actorUserId))
            {
                return ApiErrors.Forbidden("User is not allowed to manage the item image.");
            }

            var previousObjectKey = item.ImageKey;
            item.ImageKey = objectKey;
            await dbContext.SaveChangesAsync(cancellationToken);

            await TryDeletePreviousObjectAsync(
                previousObjectKey,
                objectKey,
                storageService,
                loggerFactory.CreateLogger("Items.Image"),
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

            var item = await dbContext.Items.FirstOrDefaultAsync(foundItem => foundItem.Id == id);
            if (item is null)
            {
                return ApiErrors.NotFound("Item");
            }

            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == item.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            if (string.IsNullOrWhiteSpace(item.ImageKey))
            {
                return ApiErrors.NotFound("ItemImage");
            }

            var downloadUrl = storageService.GeneratePresignedGetUrl(item.ImageKey, DownloadPresignTtlSeconds);
            return Results.Ok(new ItemImageResponseDto(item.ImageKey, downloadUrl));
        })
            .Produces<ItemImageResponseDto>(StatusCodes.Status200OK)
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

            var item = await dbContext.Items.FirstOrDefaultAsync(foundItem => foundItem.Id == id, cancellationToken);
            if (item is null)
            {
                return ApiErrors.NotFound("Item");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(foundMembership =>
                foundMembership.CommunityId == item.CommunityId
                && foundMembership.UserId == actorUserId
                && foundMembership.Status == MembershipStatus.Active,
                cancellationToken);
            if (membership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            if (!CanManageItem(membership, item.OwnerUserId, actorUserId))
            {
                return ApiErrors.Forbidden("User is not allowed to manage the item image.");
            }

            var previousObjectKey = item.ImageKey;
            if (string.IsNullOrWhiteSpace(previousObjectKey))
            {
                return Results.NoContent();
            }

            item.ImageKey = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            var logger = loggerFactory.CreateLogger("Items.Image");
            try
            {
                await storageService.DeleteObjectAsync(previousObjectKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed deleting item image from R2. key: {ObjectKey}", previousObjectKey);
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static bool CanManageItem(Membership membership, string ownerUserId, string actorUserId)
    {
        return MembershipRolePolicy.CanModerateContent(membership.Role)
            || string.Equals(ownerUserId, actorUserId, StringComparison.Ordinal);
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    private static (string Sort, string Order) ParseSort(string? sort)
    {
        var normalized = string.IsNullOrWhiteSpace(sort)
            ? "createdat_desc"
            : sort.Trim().ToLowerInvariant();

        return normalized switch
        {
            "createdat_asc" => ("createdAt", "asc"),
            "name_asc" => ("name", "asc"),
            "name_desc" => ("name", "desc"),
            _ => ("createdAt", "desc")
        };
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
            logger.LogWarning(ex, "Failed deleting previous item image from R2. key: {ObjectKey}", previousObjectKey);
        }
    }

    public sealed record ItemImageResponseDto(string ObjectKey, string DownloadUrl);
}
