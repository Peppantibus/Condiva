using System.Text.RegularExpressions;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Storage.Dtos;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Features.Storage.Endpoints;

public static class StorageEndpoints
{
    private const int DownloadPresignTtlSeconds = 300;
    private const int MaxResolveBatchSize = 100;
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/pdf"
    };
    private static readonly Regex InvalidFileNameCharsRegex = new(@"[^a-zA-Z0-9._-]+", RegexOptions.Compiled);

    public static IEndpointRouteBuilder MapStorageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/storage");
        group.RequireAuthorization();
        group.WithTags("Storage");

        group.MapPost("/presign", (
            StoragePresignRequestDto body,
            IR2StorageService storageService) =>
        {
            if (string.IsNullOrWhiteSpace(body.FileName))
            {
                return ApiErrors.Required(nameof(body.FileName));
            }

            if (string.IsNullOrWhiteSpace(body.ContentType))
            {
                return ApiErrors.Required(nameof(body.ContentType));
            }

            var contentType = body.ContentType.Trim();
            if (!AllowedContentTypes.Contains(contentType))
            {
                return ApiErrors.Invalid("Unsupported contentType.");
            }

            var sanitizedFileName = SanitizeFileName(body.FileName);
            var objectKey = BuildObjectKey(sanitizedFileName);
            var expiresIn = storageService.DefaultPresignTtlSeconds;
            var uploadUrl = storageService.GeneratePresignedPutUrl(objectKey, contentType, expiresIn);

            var payload = new StoragePresignResponseDto(objectKey, uploadUrl, expiresIn);
            return Results.Ok(payload);
        })
            .Produces<StoragePresignResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPost("/resolve", (
            StorageResolveRequestDto body,
            IR2StorageService storageService) =>
        {
            if (body.ObjectKeys is null || body.ObjectKeys.Count == 0)
            {
                return ApiErrors.Required(nameof(body.ObjectKeys));
            }

            if (body.ObjectKeys.Count > MaxResolveBatchSize)
            {
                return ApiErrors.Invalid($"Too many objectKeys. Max {MaxResolveBatchSize}.");
            }

            var normalizedKeys = new List<string>(body.ObjectKeys.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var objectKey in body.ObjectKeys)
            {
                if (!TryNormalizeObjectKey(objectKey, out var normalizedKey))
                {
                    return ApiErrors.Invalid("Invalid key.");
                }

                if (seen.Add(normalizedKey))
                {
                    normalizedKeys.Add(normalizedKey);
                }
            }

            var items = normalizedKeys
                .Select(objectKey => new StorageResolveItemDto(
                    objectKey,
                    storageService.GeneratePresignedGetUrl(objectKey, DownloadPresignTtlSeconds)))
                .ToList();
            var payload = new StorageResolveResponseDto(items, DownloadPresignTtlSeconds);
            return Results.Ok(payload);
        })
            .Produces<StorageResolveResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet("/{**key}", (
            string key,
            IR2StorageService storageService) =>
        {
            if (!TryNormalizeObjectKey(key, out var normalizedKey))
            {
                return ApiErrors.Invalid("Invalid key.");
            }

            var downloadUrl = storageService.GeneratePresignedGetUrl(normalizedKey, DownloadPresignTtlSeconds);
            var payload = new StorageDownloadResponseDto(downloadUrl);
            return Results.Ok(payload);
        })
            .Produces<StorageDownloadResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapDelete("/{**key}", async (
            string key,
            IR2StorageService storageService,
            CancellationToken cancellationToken) =>
        {
            if (!TryNormalizeObjectKey(key, out var normalizedKey))
            {
                return ApiErrors.Invalid("Invalid key.");
            }

            await storageService.DeleteObjectAsync(normalizedKey, cancellationToken);
            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static string BuildObjectKey(string sanitizedFileName)
    {
        var now = DateTime.UtcNow;
        return $"dev/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}_{sanitizedFileName}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return "file";
        }

        var sanitized = InvalidFileNameCharsRegex.Replace(baseName, "_")
            .Trim('.', '_', '-');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "file";
        }

        if (sanitized.Length > 128)
        {
            sanitized = sanitized[..128];
        }

        return sanitized;
    }

    private static bool TryNormalizeObjectKey(string? objectKey, out string normalizedKey)
    {
        normalizedKey = string.Empty;
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return false;
        }

        var trimmed = objectKey.Trim();
        if (trimmed.StartsWith('/') || trimmed.Contains('\\') || trimmed.Contains("//"))
        {
            return false;
        }

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments.Any(segment => segment is "." or ".."))
        {
            return false;
        }

        normalizedKey = string.Join('/', segments);
        return true;
    }
}
