using Condiva.Api.Common.Errors;
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
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(communityId))
            {
                return ApiErrors.Required(nameof(communityId));
            }

            var result = await repository.GetAllAsync(communityId, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Item, ItemListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        })
            .Produces<List<ItemListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateItemRequestDto body,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
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

            var result = await repository.CreateAsync(model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Created($"/api/items/{payload.Id}", payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", async (
            string id,
            UpdateItemRequestDto body,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
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
            return Results.Ok(payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IItemRepository repository) =>
        {
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
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            if (!CanManageItem(membership, item.OwnerUserId, actorUserId))
            {
                return ApiErrors.Invalid("User is not allowed to manage the item image.");
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
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            if (!CanManageItem(membership, item.OwnerUserId, actorUserId))
            {
                return ApiErrors.Invalid("User is not allowed to manage the item image.");
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
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            if (!CanManageItem(membership, item.OwnerUserId, actorUserId))
            {
                return ApiErrors.Invalid("User is not allowed to manage the item image.");
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
        return membership.Role is MembershipRole.Owner or MembershipRole.Moderator
            || string.Equals(ownerUserId, actorUserId, StringComparison.Ordinal);
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
            logger.LogWarning(ex, "Failed deleting previous item image from R2. key: {ObjectKey}", previousObjectKey);
        }
    }

    public sealed record ItemImageResponseDto(string ObjectKey, string DownloadUrl);
}
