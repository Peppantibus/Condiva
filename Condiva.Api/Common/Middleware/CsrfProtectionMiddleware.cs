using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Auth.Configuration;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Security;
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
        "/api/auth/refresh"
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
        _allowedOrigins = new HashSet<string>(OriginAllowlist.Resolve(configuration), StringComparer.OrdinalIgnoreCase);
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
            await ApiErrors.Forbidden("Origin or Referer is not allowed.").ExecuteAsync(context);
            return;
        }

        if (!IsCsrfTokenExemptPath(context.Request.Path)
            && !HasValidDoubleSubmitToken(context.Request, _cookieSettings))
        {
            await ApiErrors.Forbidden("CSRF token is missing or invalid.").ExecuteAsync(context);
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

}

public static class CsrfProtectionExtensions
{
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CsrfProtectionMiddleware>();
    }
}
