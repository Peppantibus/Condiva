using Microsoft.AspNetCore.Http;

namespace Condiva.Api.Common.Auth.Configuration;

public sealed class AuthCookieSettings
{
    public bool RequireSecure { get; set; } = true;
    public AuthCookieDefinition AccessToken { get; set; } = new()
    {
        Name = "__Host-condiva_at",
        Path = "/",
        HttpOnly = true,
        SameSite = "Lax",
        MaxAgeMinutes = 15
    };
    public AuthCookieDefinition RefreshToken { get; set; } = new()
    {
        Name = "__Secure-condiva_rt",
        Path = "/api/auth/refresh",
        HttpOnly = true,
        SameSite = "Strict",
        MaxAgeMinutes = 60 * 24 * 30
    };
    public AuthCookieDefinition CsrfToken { get; set; } = new()
    {
        Name = "__Host-condiva_csrf",
        Path = "/",
        HttpOnly = false,
        SameSite = "Strict",
        MaxAgeMinutes = 60 * 24 * 30
    };
}

public sealed class AuthCookieDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public string? Domain { get; set; }
    public bool HttpOnly { get; set; } = true;
    public string SameSite { get; set; } = "Lax";
    public int MaxAgeMinutes { get; set; } = 15;

    public SameSiteMode ResolveSameSite()
    {
        return SameSite?.Trim().ToLowerInvariant() switch
        {
            "strict" => SameSiteMode.Strict,
            "none" => SameSiteMode.None,
            _ => SameSiteMode.Lax
        };
    }
}
