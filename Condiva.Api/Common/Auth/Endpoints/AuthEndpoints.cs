using AuthLibrary.Interfaces;
using AuthLibrary.Models;
using AuthLibrary.Models.Dto.Auth;
using Condiva.Api.Common.Auth.Models;
using Microsoft.AspNetCore.Routing;

namespace Condiva.Api.Common.Auth.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth");
        group.WithTags("Auth");

        endpoints.MapPost("/api/auth/login",
            async (LoginRequest body, IAuthService<User> auth) =>
            {
                var result = await auth.Login(body.Username, body.Password);
                return MapResult(result);
            })
            .WithTags("Auth");

        endpoints.MapPost("/api/auth/register",
            async (
                RegisterRequest body,
                IAuthService<User> auth,
                IConfiguration configuration,
                IAuthRepository<User> repository) =>
            {
                var autoVerify = configuration.GetValue<bool>("AuthSettings:AutoVerifyEmail");
                var user = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Username = body.Username,
                    Email = body.Email,
                    Password = body.Password,
                    Name = body.Name,
                    LastName = body.LastName,
                    EmailVerified = autoVerify,
                    PasswordUpdatedAt = DateTime.UtcNow
                };
                var result = await auth.AddUser(user);
                if (!result.IsSuccess)
                {
                    return Results.BadRequest(new { error = result.Error });
                }

                if (autoVerify)
                {
                    user.EmailVerified = true;
                    await repository.UpdateUserAsync(user);
                    await repository.RemoveEmailVerifiedTokensByUserIdAsync(user.Id);
                    await repository.SaveChangesAsync();
                }

                return Results.Ok();
            })
            .WithTags("Auth");

        endpoints.MapPost("/api/auth/recovery",
            async (RecoveryRequest body, IAuthService<User> auth) =>
            {
                var result = await auth.RecoveryPassword(body.Email);
                return MapResult(result);
            })
            .WithTags("Auth");

        endpoints.MapGet("/api/auth/reset/redirect",
            async (string token, IAuthService<User> auth) =>
            {
                var result = await auth.ResetPasswordRedirect(token);
                return MapResult(result);
            })
            .WithTags("Auth");

        endpoints.MapPost("/api/auth/reset",
            async (ResetPasswordDto body, IAuthService<User> auth) =>
            {
                var result = await auth.ResetPassword(body);
                return MapResult(result);
            })
            .WithTags("Auth");

        endpoints.MapGet("/api/auth/verify",
            async (string token, IAuthService<User> auth) =>
            {
                var result = await auth.VerifyMail(token);
                return MapResult(result);
            })
            .WithTags("Auth");

        endpoints.MapPost("/api/auth/verify/resend",
            async (ResendVerificationRequest body, IAuthService<User> auth) =>
            {
                var result = await auth.ResendVerificationEmail(body.Email);
                return MapResult(result);
            })
            .WithTags("Auth");

        endpoints.MapPost("/api/auth/refresh",
            async (RefreshTokenRequest body, ITokenService<User> tokens) =>
            {
                var result = await tokens.TryRefreshToken(body.Token);
                return result.IsSuccess
                    ? Results.Ok(result.Value)
                    : Results.Unauthorized();
            })
            .WithTags("Auth");

        return endpoints;
    }

    private static IResult MapResult(Result result)
    {
        return result.IsSuccess
            ? Results.Ok()
            : Results.BadRequest(new { error = result.Error });
    }

    private static IResult MapResult<T>(Result<T> result)
    {
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.BadRequest(new { error = result.Error });
    }

    public sealed record LoginRequest(string Username, string Password);
    public sealed record RegisterRequest(string Username, string Email, string Password, string Name, string LastName);
    public sealed record RecoveryRequest(string Email);
    public sealed record ResendVerificationRequest(string Email);
    public sealed record RefreshTokenRequest(string Token);
}
