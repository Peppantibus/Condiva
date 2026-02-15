using System.Text.RegularExpressions;

namespace Condiva.Api.Infrastructure.Storage;

public static class StorageImageKeyHelper
{
    private static readonly Regex InvalidFileNameCharsRegex = new(@"[^a-zA-Z0-9._-]+", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    public static bool IsAllowedImageContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        return AllowedImageContentTypes.Contains(contentType.Trim());
    }

    public static string SanitizeFileName(string fileName)
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

    public static string BuildScopedObjectKey(string scope, string sanitizedFileName)
    {
        var normalizedScope = scope.Trim('/');
        var now = DateTime.UtcNow;
        return $"{normalizedScope}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}_{sanitizedFileName}";
    }

    public static bool TryNormalizeObjectKey(string? objectKey, out string normalizedKey)
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

    public static bool IsScopedKey(string key, string requiredScope)
    {
        var normalizedScope = requiredScope.Trim('/');
        return key.StartsWith($"{normalizedScope}/", StringComparison.Ordinal);
    }
}
