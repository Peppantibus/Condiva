using System.Diagnostics.CodeAnalysis;

namespace Condiva.Api.Common.Security;

public static class OriginAllowlist
{
    public static string[] Resolve(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .ToList() ?? [];

        var frontendUrl = configuration.GetValue<string>("AuthSettings:FrontendUrl");
        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            origins.Add(frontendUrl.Trim());
        }

        var normalizedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in origins)
        {
            if (origin.Contains('*', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must not contain wildcard values when credentials are enabled.");
            }

            if (!TryNormalize(origin, out var normalized))
            {
                throw new InvalidOperationException(
                    $"Origin '{origin}' is not a valid absolute http(s) origin.");
            }

            normalizedOrigins.Add(normalized);
        }

        return normalizedOrigins.ToArray();
    }

    public static bool TryNormalize(string value, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
