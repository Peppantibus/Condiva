using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Condiva.Api.Common.Concurrency;

public static class EntityTagHelper
{
    public static void Set(HttpContext context, object entity)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Headers.ETag = Compute(entity);
    }

    public static bool IsIfMatchSatisfied(string? ifMatchHeader, object currentEntity)
    {
        if (string.IsNullOrWhiteSpace(ifMatchHeader))
        {
            return true;
        }

        var currentTag = NormalizeTag(Compute(currentEntity));
        var candidates = ifMatchHeader
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var candidate in candidates)
        {
            if (candidate == "*")
            {
                return true;
            }

            if (string.Equals(NormalizeTag(candidate), currentTag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string Compute(object entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        var canonical = BuildCanonicalPayload(entity);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return $"\"{Convert.ToHexString(hash)}\"";
    }

    private static string BuildCanonicalPayload(object entity)
    {
        var values = entity
            .GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && IsScalar(property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}:{FormatValue(property.GetValue(entity), property.PropertyType)}");

        return string.Join("|", values);
    }

    private static bool IsScalar(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(TimeSpan);
    }

    private static string FormatValue(object? value, Type declaredType)
    {
        if (value is null)
        {
            return "<null>";
        }

        var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type == typeof(DateTime))
        {
            var dateTime = (DateTime)value;
            var normalized = dateTime.Kind == DateTimeKind.Utc
                ? dateTime
                : dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime();
            return normalized.ToString("O", CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTimeOffset))
        {
            return ((DateTimeOffset)value).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        }

        if (type.IsEnum)
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return value.ToString() ?? string.Empty;
    }

    private static string NormalizeTag(string tag)
    {
        var normalized = tag.Trim();
        if (normalized.StartsWith("W/", StringComparison.Ordinal))
        {
            normalized = normalized[2..].Trim();
        }

        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"')
        {
            normalized = normalized[1..^1];
        }

        return normalized;
    }
}
