using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Auth.Configuration;
using Microsoft.Extensions.Options;

namespace Condiva.Api.Common.Middleware;

public sealed class CsrfProtectionMiddleware
{
    private static readonly string[] TokenExemptPaths =
    [
        "/api/auth/login",
        "/api/auth/google",
        "/api/auth/register",
        "/api/auth/recovery",
        "/api/auth/reset",
        "/api/auth/verify/resend",
        "/api/auth/refresh",
        "/api/auth/logout"
    ];

    private readonly RequestDelegate _next;
    private readonly AuthCookieSettings _cookieSettings;
    private readonly HashSet<string> _allowedOrigins;

    public CsrfProtectionMiddleware(
        RequestDelegate next,
        IConfiguration configuration,
        IOptions<AuthCookieSettings> cookieOptions)
    {
        _next = next;
        _cookieSettings = cookieOptions.Value;
        _allowedOrigins = ResolveAllowedOrigins(configuration);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsStateChangingMethod(context.Request.Method)
            || !HasAuthCookies(context.Request, _cookieSettings))
        {
            await _next(context);
            return;
        }

        if (!HasValidOrigin(context.Request, _allowedOrigins))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "invalid_origin",
                message = "Origin or Referer is not allowed."
            });
            return;
        }

        if (!IsCsrfTokenExemptPath(context.Request.Path)
            && !HasValidDoubleSubmitToken(context.Request, _cookieSettings))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "invalid_csrf_token",
                message = "CSRF token is missing or invalid."
            });
            return;
        }

        await _next(context);
    }

    private static bool IsStateChangingMethod(string method)
    {
        return HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);
    }

    private static bool HasAuthCookies(HttpRequest request, AuthCookieSettings cookieSettings)
    {
        return request.Cookies.ContainsKey(cookieSettings.AccessToken.Name)
            || request.Cookies.ContainsKey(cookieSettings.RefreshToken.Name);
    }

    private static bool IsCsrfTokenExemptPath(PathString path)
    {
        return TokenExemptPaths.Any(exemptPath =>
            path.StartsWithSegments(exemptPath, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasValidDoubleSubmitToken(HttpRequest request, AuthCookieSettings cookieSettings)
    {
        var headerToken = request.Headers[AuthSecurityHeaders.CsrfToken].ToString();
        if (string.IsNullOrWhiteSpace(headerToken))
        {
            return false;
        }

        if (!request.Cookies.TryGetValue(cookieSettings.CsrfToken.Name, out var cookieToken))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(cookieToken)
            && string.Equals(headerToken, cookieToken, StringComparison.Ordinal);
    }

    private static bool HasValidOrigin(HttpRequest request, HashSet<string> allowedOrigins)
    {
        var originValue = request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(originValue))
        {
            return IsAllowedOrigin(originValue, request, allowedOrigins);
        }

        var refererValue = request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(refererValue))
        {
            return IsAllowedOrigin(refererValue, request, allowedOrigins);
        }

        return false;
    }

    private static bool IsAllowedOrigin(
        string originOrReferer,
        HttpRequest request,
        HashSet<string> allowedOrigins)
    {
        if (!Uri.TryCreate(originOrReferer, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var candidate = uri.GetLeftPart(UriPartial.Authority);
        if (allowedOrigins.Contains(candidate))
        {
            return true;
        }

        var requestOrigin = $"{request.Scheme}://{request.Host.Value}";
        return string.Equals(candidate, requestOrigin, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ResolveAllowedOrigins(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .ToArray() ?? [];

        if (origins.Length == 0)
        {
            var frontendUrl = configuration.GetValue<string>("AuthSettings:FrontendUrl");
            if (!string.IsNullOrWhiteSpace(frontendUrl))
            {
                origins = [frontendUrl.Trim()];
            }
        }

        var normalizedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var origin in origins)
        {
            if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                normalizedOrigins.Add(uri.GetLeftPart(UriPartial.Authority));
            }
        }

        return normalizedOrigins;
    }
}

public static class CsrfProtectionExtensions
{
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CsrfProtectionMiddleware>();
    }
}
