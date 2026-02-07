using System.Net.Mail;
using AuthLibrary.Interfaces;
using AuthLibrary.Models;
using AuthLibrary.Models.Dto.Auth;
using Condiva.Api.Common.Auth.Models;
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
                async (LoginRequest body, IAuthService<User> auth) =>
                {
                    var username = Normalize(body.Username);
                    var password = Normalize(body.Password);
                    var validationError = ValidateUsername(username) ?? ValidatePassword(password);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await auth.Login(username!, password!);
                    return MapResult(result, AuthErrorKind.InvalidCredentials);
                })
            .Produces<RefreshTokenDto>(StatusCodes.Status200OK);

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
                async (RefreshTokenRequest body, ITokenService<User> tokens) =>
                {
                    var refreshToken = Normalize(body.RefreshToken ?? body.Token);
                    var validationError = ValidateRefreshToken(refreshToken);
                    if (validationError is not null)
                    {
                        return validationError;
                    }

                    var result = await tokens.TryRefreshToken(refreshToken!);
                    return result.IsSuccess
                        ? HttpResults.Ok(result.Value)
                        : ErrorResult(AuthErrorKind.InvalidRefreshToken, result.Error ?? "Refresh token is invalid.");
                })
            .Produces<RefreshTokenDto>(StatusCodes.Status200OK);

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
}
