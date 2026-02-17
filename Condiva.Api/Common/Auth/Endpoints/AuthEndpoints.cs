using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using AuthLibrary.Interfaces;
using AuthLibrary.Models;
using AuthLibrary.Models.Dto.Auth;
using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Auth.Configuration;
using Condiva.Api.Common.Auth.Models;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Routing;
using HttpResults = Microsoft.AspNetCore.Http.Results;

namespace Condiva.Api.Common.Auth.Endpoints;

public static class AuthEndpoints
{
    private const int MinPasswordLength = 8;
    private const int MinTokenLength = 32;
    private const int MaxTokenLength = 2048;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .WithTags("Auth")
            .RequireRateLimiting("auth");

        group.MapPost("/login",
                async (
                    LoginRequest body,
                    IAuthService<User> auth,
                    HttpContext httpContext,
                    IOptions<AuthCookieSettings> authCookieOptions) =>
                {
                    var username = Normalize(body.Username);
                    var password = Normalize(body.Password);
                    var validationError = ValidateUsername(username) ?? ValidatePassword(password);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.Login(username!, password!);
                    if (!result.IsSuccess)
                    {
                        return ErrorResult(AuthErrorKind.InvalidCredentials, result.Error ?? "Login failed.");
                    }

                    SetAuthCookies(httpContext, result.Value!, authCookieOptions.Value);
                    return HttpResults.Ok(ToAuthSessionResponse(result.Value!));
                })
            .Produces<AuthSessionResponseDto>(StatusCodes.Status200OK);

        group.MapPost("/google",
                async (
                    GoogleLoginRequest body,
                    IAuthService<User> auth,
                    HttpContext httpContext,
                    IOptions<AuthCookieSettings> authCookieOptions,
                    ILoggerFactory loggerFactory) =>
                {
                    var logger = loggerFactory.CreateLogger("Auth.GoogleLogin");
                    var idToken = Normalize(body.IdToken)
                        ?? Normalize(body.Credential)
                        ?? Normalize(body.Token);
                    var expectedNonce = Normalize(body.ExpectedNonce) ?? Normalize(body.Nonce);
                    var validationError = ValidateToken(idToken);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.ExternalLoginWithGoogle(idToken!, expectedNonce);
                    if (!result.IsSuccess)
                    {
                        var error = result.Error ?? "Google login failed.";
                        var errorCode = result.GetType().GetProperty("ErrorCode")?.GetValue(result)?.ToString();
                        logger.LogWarning("Google login failed: {Error}", error);
                        if (!string.IsNullOrWhiteSpace(errorCode))
                        {
                            logger.LogWarning("Google login error code: {ErrorCode}", errorCode);
                        }

                        return ErrorResult(AuthErrorKind.InvalidToken, error);
                    }

                    SetAuthCookies(httpContext, result.Value!, authCookieOptions.Value);
                    return HttpResults.Ok(ToAuthSessionResponse(result.Value!));
                })
            .Produces<AuthSessionResponseDto>(StatusCodes.Status200OK);

        group.MapPost("/register",
                async (
                    RegisterRequest body,
                    IAuthService<User> auth,
                    IConfiguration configuration,
                    IAuthRepository<User> repository) =>
                {
                    var username = Normalize(body.Username);
                    var email = Normalize(body.Email);
                    var password = Normalize(body.Password);
                    var validationError = ValidateUsername(username)
                        ?? ValidateEmail(email)
                        ?? ValidatePassword(password);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var autoVerify = configuration.GetValue<bool>("AuthSettings:AutoVerifyEmail");
                    var user = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Username = username!,
                        Email = email!,
                        Password = password!,
                        Name = body.Name,
                        LastName = body.LastName,
                        EmailVerified = autoVerify,
                        PasswordUpdatedAt = DateTime.UtcNow
                    };

                    var result = await auth.AddUser(user);
                    if (!result.IsSuccess)
                    {
                        var kind = ResolveAuthErrorKind(result.Error, AuthErrorKind.Validation);
                        return ErrorResult(kind, result.Error ?? "Registration failed.");
                    }

                    if (autoVerify)
                    {
                        user.EmailVerified = true;
                        await repository.UpdateUserAsync(user);
                        await repository.RemoveEmailVerifiedTokensByUserIdAsync(user.Id);
                        await repository.SaveChangesAsync();
                    }

                    return HttpResults.Ok();
                })
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/recovery",
                async (RecoveryRequest body, IAuthService<User> auth) =>
                {
                    var email = Normalize(body.Email);
                    var validationError = ValidateEmail(email);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.RecoveryPassword(email!);
                    return result.IsSuccess ? HttpResults.Ok() : HttpResults.Ok();
                })
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/reset/redirect",
                async (string token, IAuthService<User> auth) =>
                {
                    var normalizedToken = Normalize(token);
                    var validationError = ValidateToken(normalizedToken);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.ResetPasswordRedirect(normalizedToken!);
                    return MapResult(result, AuthErrorKind.InvalidToken);
                })
            .Produces<bool>(StatusCodes.Status200OK);

        group.MapPost("/reset",
                async (ResetPasswordDto body, IAuthService<User> auth) =>
                {
                    var token = ExtractStringProperty(body, "Token");
                    var password = ExtractStringProperty(body, "Password") ?? ExtractStringProperty(body, "NewPassword");
                    var validationError = ValidateToken(token) ?? ValidatePassword(Normalize(password));
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.ResetPassword(body);
                    return MapResult(result, AuthErrorKind.InvalidToken);
                })
            .Produces<bool>(StatusCodes.Status200OK);

        group.MapGet("/verify",
                async (string token, IAuthService<User> auth) =>
                {
                    var validationError = ValidateToken(Normalize(token));
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.VerifyMail(token);
                    return MapResult(result, AuthErrorKind.InvalidToken);
                })
            .Produces<bool>(StatusCodes.Status200OK);

        group.MapPost("/verify/resend",
                async (ResendVerificationRequest body, IAuthService<User> auth) =>
                {
                    var email = Normalize(body.Email);
                    var validationError = ValidateEmail(email);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.ResendVerificationEmail(email!);
                    return result.IsSuccess ? HttpResults.Ok() : HttpResults.Ok();
                })
            .Produces(StatusCodes.Status200OK);

        group.MapPost("/refresh",
                async (
                    RefreshTokenRequest? body,
                    ITokenService<User> tokens,
                    HttpContext httpContext,
                    IOptions<AuthCookieSettings> authCookieOptions,
                    IConfiguration configuration,
                    ILoggerFactory loggerFactory) =>
                {
                    var logger = loggerFactory.CreateLogger("Auth.Refresh");
                    var cookieSettings = authCookieOptions.Value;
                    var refreshTokenFromCookie = ReadCookie(httpContext.Request, cookieSettings.RefreshToken.Name);
                    var refreshToken = Normalize(refreshTokenFromCookie)
                        ?? Normalize(body?.RefreshToken ?? body?.Token);
                    var validationError = ValidateRefreshToken(refreshToken);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await tokens.TryRefreshToken(refreshToken!);
                    if (!result.IsSuccess)
                    {
                        var clearCookiesOnFailure =
                            configuration.GetValue<bool?>("AuthSettings:ClearCookiesOnRefreshFailure") ?? true;
                        if (clearCookiesOnFailure)
                        {
                            ClearAuthCookies(httpContext, cookieSettings);
                        }
                        else
                        {
                            logger.LogInformation(
                                "Refresh token failed; preserving cookies because AuthSettings:ClearCookiesOnRefreshFailure=false.");
                        }

                        return ErrorResult(
                            AuthErrorKind.InvalidRefreshToken,
                            result.Error ?? "Refresh token is invalid.");
                    }

                    SetAuthCookies(httpContext, result.Value!, cookieSettings);
                    return HttpResults.Ok(ToAuthSessionResponse(result.Value!));
                })
            .Produces<AuthSessionResponseDto>(StatusCodes.Status200OK);

        group.MapGet("/csrf",
                (HttpContext httpContext, IOptions<AuthCookieSettings> authCookieOptions) =>
                {
                    var csrfToken = RotateCsrfToken(httpContext, authCookieOptions.Value);
                    return HttpResults.Ok(new CsrfTokenResponse(csrfToken));
                })
            .RequireAuthorization()
            .Produces<CsrfTokenResponse>(StatusCodes.Status200OK);

        group.MapPost("/logout",
                async (
                    ClaimsPrincipal user,
                    HttpContext httpContext,
                    IAuthRepository<User> repository,
                    IOptions<AuthCookieSettings> authCookieOptions) =>
                {
                    var userId = CurrentUser.GetUserId(user);
                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        await repository.RemoveRefreshTokensByUserIdAsync(userId);
                        await repository.SaveChangesAsync();
                    }

                    ClearAuthCookies(httpContext, authCookieOptions.Value);
                    return HttpResults.Ok();
                })
            .RequireAuthorization()
            .Produces(StatusCodes.Status200OK);

        return endpoints;
    }

    private static IResult? ValidateRefreshToken(string? token)
    {
        var validationError = ValidateToken(token);
        return validationError is null
            ? null
            : ErrorResult(AuthErrorKind.InvalidRefreshToken, "Refresh token is invalid.");
    }

    private static IResult MapResult<T>(Result<T> result, AuthErrorKind errorKind)
    {
        return result.IsSuccess
            ? HttpResults.Ok(result.Value)
            : ErrorResult(errorKind, result.Error ?? "Operation failed.");
    }

    private static IResult ErrorResult(AuthErrorKind kind, string message)
    {
        var error = new ApiError(GetErrorCode(kind), message);
        var payload = new ErrorResponse(error);

        return kind switch
        {
            AuthErrorKind.InvalidCredentials => HttpResults.Json(payload, statusCode: StatusCodes.Status401Unauthorized),
            AuthErrorKind.InvalidRefreshToken => HttpResults.Json(payload, statusCode: StatusCodes.Status401Unauthorized),
            AuthErrorKind.Conflict => HttpResults.Conflict(payload),
            AuthErrorKind.Validation => HttpResults.BadRequest(payload),
            AuthErrorKind.InvalidToken => HttpResults.BadRequest(payload),
            _ => HttpResults.BadRequest(payload)
        };
    }

    private static AuthErrorKind ResolveAuthErrorKind(string? errorMessage, AuthErrorKind fallback)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return fallback;
        }

        if (errorMessage.Contains("exist", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("already", StringComparison.OrdinalIgnoreCase)
            || errorMessage.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            return AuthErrorKind.Conflict;
        }

        return fallback;
    }

    private static string GetErrorCode(AuthErrorKind kind)
    {
        return kind switch
        {
            AuthErrorKind.InvalidCredentials => "invalid_credentials",
            AuthErrorKind.InvalidRefreshToken => "invalid_refresh_token",
            AuthErrorKind.InvalidToken => "invalid_token",
            AuthErrorKind.Conflict => "conflict",
            AuthErrorKind.Validation => "validation_error",
            _ => "auth_error"
        };
    }

    private static IResult? ValidateUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return ErrorResult(AuthErrorKind.Validation, "Username is required.");
        }

        if (username.Length < 3)
        {
            return ErrorResult(AuthErrorKind.Validation, "Username must be at least 3 characters.");
        }

        return null;
    }

    private static IResult? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return ErrorResult(AuthErrorKind.Validation, "Email is required.");
        }

        if (!IsValidEmail(email))
        {
            return ErrorResult(AuthErrorKind.Validation, "Email format is invalid.");
        }

        return null;
    }

    private static IResult? ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return ErrorResult(AuthErrorKind.Validation, "Password is required.");
        }

        if (password.Length < MinPasswordLength)
        {
            return ErrorResult(AuthErrorKind.Validation, $"Password must be at least {MinPasswordLength} characters.");
        }

        return null;
    }

    private static IResult? ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return ErrorResult(AuthErrorKind.Validation, "Token is required.");
        }

        if (token.Length < MinTokenLength || token.Length > MaxTokenLength)
        {
            return ErrorResult(
                AuthErrorKind.Validation,
                $"Token length must be between {MinTokenLength} and {MaxTokenLength} characters.");
        }

        if (token.Any(char.IsWhiteSpace))
        {
            return ErrorResult(AuthErrorKind.Validation, "Token format is invalid.");
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractStringProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property is null || property.PropertyType != typeof(string))
        {
            return null;
        }

        return property.GetValue(instance) as string;
    }

    private static DateTime? ExtractDateTimeProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(instance);
        return value switch
        {
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
            _ => null
        };
    }

    private static int? ExtractIntegerProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property is null)
        {
            return null;
        }

        var value = property.GetValue(instance);
        return value switch
        {
            int intValue => intValue,
            long longValue => checked((int)longValue),
            short shortValue => shortValue,
            byte byteValue => byteValue,
            _ => null
        };
    }

    private static DateTime? ExtractJwtExpiry(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            var payload = segments[1]
                .Replace('-', '+')
                .Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
            }

            var payloadBytes = Convert.FromBase64String(payload);
            using var payloadJson = JsonDocument.Parse(payloadBytes);
            if (!payloadJson.RootElement.TryGetProperty("exp", out var expProperty))
            {
                return null;
            }

            long unixSeconds;
            if (expProperty.ValueKind == JsonValueKind.Number && expProperty.TryGetInt64(out var numericExp))
            {
                unixSeconds = numericExp;
            }
            else if (expProperty.ValueKind == JsonValueKind.String
                     && long.TryParse(expProperty.GetString(), out var stringExp))
            {
                unixSeconds = stringExp;
            }
            else
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
        }
        catch
        {
            return null;
        }
    }

    private static AuthSessionUserDto? BuildAuthSessionUser(RefreshTokenDto tokenPayload)
    {
        var userProperty = tokenPayload.GetType().GetProperty("User");
        var userValue = userProperty?.GetValue(tokenPayload);
        if (userValue is null)
        {
            return null;
        }

        var id = Normalize(ExtractStringProperty(userValue, "Id"));
        var username = Normalize(
            ExtractStringProperty(userValue, "Username")
            ?? ExtractStringProperty(userValue, "UserName"));
        var email = Normalize(ExtractStringProperty(userValue, "Email"));
        var name = Normalize(
            ExtractStringProperty(userValue, "Name")
            ?? ExtractStringProperty(userValue, "GivenName"));
        var lastName = Normalize(
            ExtractStringProperty(userValue, "LastName")
            ?? ExtractStringProperty(userValue, "FamilyName"));

        if (string.IsNullOrWhiteSpace(id)
            && string.IsNullOrWhiteSpace(username)
            && string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new AuthSessionUserDto(
            id ?? string.Empty,
            username ?? string.Empty,
            email ?? string.Empty,
            name,
            lastName);
    }

    private static DateTime NormalizeDateTime(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => value
        };
    }

    private static DateTime? NormalizeDateTime(DateTime? value)
    {
        return value.HasValue ? NormalizeDateTime(value.Value) : null;
    }

    private static string? ReadCookie(HttpRequest request, string cookieName)
    {
        return request.Cookies.TryGetValue(cookieName, out var value) ? value : null;
    }

    private static AuthSessionResponseDto ToAuthSessionResponse(RefreshTokenDto tokenPayload)
    {
        var accessToken = Normalize(tokenPayload.AccessToken?.Token) ?? string.Empty;
        var accessTokenPayload = tokenPayload.AccessToken;
        var accessTokenExpiresAt = NormalizeDateTime(
            (accessTokenPayload is null ? null : ExtractDateTimeProperty(accessTokenPayload, "ExpiresAt"))
            ?? (accessTokenPayload is null ? null : ExtractDateTimeProperty(accessTokenPayload, "ExpireAt"))
            ?? ExtractJwtExpiry(accessToken)
            ?? DateTime.UtcNow.AddSeconds(Math.Max(
                (accessTokenPayload is null ? null : ExtractIntegerProperty(accessTokenPayload, "ExpiresIn"))
                ?? ExtractIntegerProperty(tokenPayload, "ExpiresIn")
                ?? 0,
                0)));
        var expiresIn = Math.Max(
            (int)Math.Ceiling((accessTokenExpiresAt - DateTime.UtcNow).TotalSeconds),
            0);
        var refreshTokenExpiresAt = NormalizeDateTime(ExtractDateTimeProperty(tokenPayload, "RefreshTokenExpiresAt"));
        var user = BuildAuthSessionUser(tokenPayload);

        return new AuthSessionResponseDto(
            accessToken,
            expiresIn,
            "Bearer",
            accessTokenExpiresAt,
            refreshTokenExpiresAt,
            user);
    }

    private static void SetAuthCookies(
        HttpContext httpContext,
        RefreshTokenDto tokenPayload,
        AuthCookieSettings cookieSettings)
    {
        var accessToken = Normalize(tokenPayload.AccessToken?.Token);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            AppendCookie(httpContext, cookieSettings.AccessToken, accessToken, cookieSettings.RequireSecure);
        }

        var refreshToken = Normalize(tokenPayload.NewRefreshToken);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            AppendCookie(httpContext, cookieSettings.RefreshToken, refreshToken, cookieSettings.RequireSecure);
        }

        RotateCsrfToken(httpContext, cookieSettings);
    }

    private static string RotateCsrfToken(HttpContext httpContext, AuthCookieSettings cookieSettings)
    {
        var csrfToken = GenerateCsrfToken();
        AppendCookie(httpContext, cookieSettings.CsrfToken, csrfToken, cookieSettings.RequireSecure);
        httpContext.Response.Headers[AuthSecurityHeaders.CsrfToken] = csrfToken;
        return csrfToken;
    }

    private static void ClearAuthCookies(HttpContext httpContext, AuthCookieSettings cookieSettings)
    {
        DeleteCookie(httpContext, cookieSettings.AccessToken, cookieSettings.RequireSecure);
        DeleteCookie(httpContext, cookieSettings.RefreshToken, cookieSettings.RequireSecure);
        DeleteCookie(httpContext, cookieSettings.CsrfToken, cookieSettings.RequireSecure);
        httpContext.Response.Headers.Remove(AuthSecurityHeaders.CsrfToken);
    }

    private static void AppendCookie(
        HttpContext httpContext,
        AuthCookieDefinition cookie,
        string value,
        bool requireSecure)
    {
        var secure = requireSecure || httpContext.Request.IsHttps;
        httpContext.Response.Cookies.Append(cookie.Name, value, CreateCookieOptions(cookie, secure));
    }

    private static void DeleteCookie(HttpContext httpContext, AuthCookieDefinition cookie, bool requireSecure)
    {
        var secure = requireSecure || httpContext.Request.IsHttps;
        httpContext.Response.Cookies.Delete(cookie.Name, CreateCookieOptions(cookie, secure));
    }

    private static CookieOptions CreateCookieOptions(AuthCookieDefinition cookie, bool secure)
    {
        return new CookieOptions
        {
            HttpOnly = cookie.HttpOnly,
            Secure = secure,
            SameSite = cookie.ResolveSameSite(),
            Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
            Domain = string.IsNullOrWhiteSpace(cookie.Domain) ? null : cookie.Domain,
            MaxAge = TimeSpan.FromMinutes(Math.Max(cookie.MaxAgeMinutes, 1))
        };
    }

    private static string GenerateCsrfToken()
    {
        Span<byte> data = stackalloc byte[32];
        RandomNumberGenerator.Fill(data);
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private enum AuthErrorKind
    {
        Unknown,
        Validation,
        InvalidCredentials,
        InvalidToken,
        InvalidRefreshToken,
        Conflict
    }

    public sealed record ApiError(string Code, string Message);
    public sealed record ErrorResponse(ApiError Error);

    public sealed record LoginRequest(string Username, string Password);
    public sealed record RegisterRequest(string Username, string Email, string Password, string Name, string LastName);
    public sealed record RecoveryRequest(string Email);
    public sealed record ResendVerificationRequest(string Email);
    public sealed record RefreshTokenRequest(string? RefreshToken, string? Token);
    public sealed record CsrfTokenResponse(string CsrfToken);
    public sealed record AuthSessionUserDto(
        string Id,
        string Username,
        string Email,
        string? Name,
        string? LastName);
    public sealed record AuthSessionResponseDto(
        string AccessToken,
        int ExpiresIn,
        string TokenType,
        DateTime ExpiresAt,
        DateTime? RefreshTokenExpiresAt,
        AuthSessionUserDto? User);
    public sealed record GoogleLoginRequest(
        string? IdToken,
        string? Credential,
        string? Token,
        string? ExpectedNonce,
        string? Nonce);
}
