using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Storage.Dtos;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Users.Endpoints;

public static class UsersEndpoints
{
    private const int DownloadPresignTtlSeconds = 300;

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/users");
        group.RequireAuthorization();
        group.WithTags("Users");

        group.MapPost("/me/profile-image/presign", async (
            StoragePresignRequestDto body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var appUser = await dbContext.Users.FirstOrDefaultAsync(foundUser => foundUser.Id == actorUserId);
            if (appUser is null)
            {
                return ApiErrors.NotFound("User");
            }

            if (string.IsNullOrWhiteSpace(body.FileName))
            {
                return ApiErrors.Required(nameof(body.FileName));
            }

            if (!StorageImageKeyHelper.IsAllowedImageContentType(body.ContentType))
            {
                return ApiErrors.Invalid("Unsupported contentType.");
            }

            var scope = $"users/{actorUserId}/profile";
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

        group.MapPost("/me/profile-image/confirm", async (
            StorageConfirmRequestDto body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            if (!StorageImageKeyHelper.TryNormalizeObjectKey(body.ObjectKey, out var objectKey))
            {
                return ApiErrors.Invalid("Invalid objectKey.");
            }

            var scope = $"users/{actorUserId}/profile";
            if (!StorageImageKeyHelper.IsScopedKey(objectKey, scope))
            {
                return ApiErrors.Invalid("objectKey is outside allowed scope.");
            }

            var appUser = await dbContext.Users.FirstOrDefaultAsync(foundUser => foundUser.Id == actorUserId, cancellationToken);
            if (appUser is null)
            {
                return ApiErrors.NotFound("User");
            }

            var previousObjectKey = appUser.ProfileImageKey;
            appUser.ProfileImageKey = objectKey;
            await dbContext.SaveChangesAsync(cancellationToken);

            await TryDeletePreviousObjectAsync(
                previousObjectKey,
                objectKey,
                storageService,
                loggerFactory.CreateLogger("Users.ProfileImage"),
                cancellationToken);

            return Results.Ok(new { objectKey });
        })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/me/profile-image", async (
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var appUser = await dbContext.Users.FirstOrDefaultAsync(foundUser => foundUser.Id == actorUserId);
            if (appUser is null)
            {
                return ApiErrors.NotFound("User");
            }

            if (string.IsNullOrWhiteSpace(appUser.ProfileImageKey))
            {
                return ApiErrors.NotFound("UserProfileImage");
            }

            var downloadUrl = storageService.GeneratePresignedGetUrl(appUser.ProfileImageKey, DownloadPresignTtlSeconds);
            return Results.Ok(new ProfileImageResponseDto(appUser.ProfileImageKey, downloadUrl));
        })
            .Produces<ProfileImageResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/me/profile-image", async (
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var appUser = await dbContext.Users.FirstOrDefaultAsync(foundUser => foundUser.Id == actorUserId, cancellationToken);
            if (appUser is null)
            {
                return ApiErrors.NotFound("User");
            }

            var previousObjectKey = appUser.ProfileImageKey;
            if (string.IsNullOrWhiteSpace(previousObjectKey))
            {
                return Results.NoContent();
            }

            appUser.ProfileImageKey = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            var logger = loggerFactory.CreateLogger("Users.ProfileImage");
            try
            {
                await storageService.DeleteObjectAsync(previousObjectKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed deleting profile image from R2. key: {ObjectKey}", previousObjectKey);
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
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
            logger.LogWarning(ex, "Failed deleting previous profile image from R2. key: {ObjectKey}", previousObjectKey);
        }
    }

    public sealed record ProfileImageResponseDto(string ObjectKey, string DownloadUrl);
}
