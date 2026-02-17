using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Condiva.Tests.Infrastructure;
using AuthLibrary.Configuration;
using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Auth.Configuration;
using Condiva.Api.Common.Auth.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Condiva.Tests;

public sealed class AuthEndpointsTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public AuthEndpointsTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithValidData_ReturnsOk()
    {
        using var client = CreateClient();
        var username = $"user-{Guid.NewGuid():N}";
        var email = $"{username}@example.com";
        var password = "Password123!";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = username,
            Email = email,
            Password = password,
            Name = "Test",
            LastName = "User"
        });

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException(
                $"Expected OK but got {response.StatusCode}. Body: {body}");
        }
    }

    [Fact]
    public async Task Login_WithInvalidPassword_ReturnsUnauthorized()
    {
        using var client = CreateClient();
        var (username, email, _) = await RegisterUserAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = email,
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithInvalidToken_ReturnsUnauthorized()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new
        {
            Token = "invalid-refresh-token"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithValidCredentials_SetsAuthCookiesAndCsrfHeader()
    {
        using var client = CreateClient();
        var (username, _, password) = await RegisterUserAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = username,
            Password = password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.AccessToken));
        Assert.True(payload.ExpiresIn > 0);
        Assert.Equal("Bearer", payload.TokenType);
        Assert.True(payload.ExpiresAt > DateTime.UtcNow.AddMinutes(-1));
        var cookieSettings = GetAuthCookieSettings();

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookieValues));
        var setCookies = setCookieValues!.ToArray();
        Assert.Contains(setCookies, cookie => HasCookieAttributes(
            cookie,
            cookieSettings.AccessToken.Name,
            BuildExpectedCookieAttributes(cookieSettings.AccessToken, cookieSettings.RequireSecure)));
        Assert.Contains(setCookies, cookie => HasCookieAttributes(
            cookie,
            cookieSettings.RefreshToken.Name,
            BuildExpectedCookieAttributes(cookieSettings.RefreshToken, cookieSettings.RequireSecure)));
        Assert.Contains(setCookies, cookie => HasCookieAttributes(
            cookie,
            cookieSettings.CsrfToken.Name,
            BuildExpectedCookieAttributes(cookieSettings.CsrfToken, cookieSettings.RequireSecure)));

        Assert.True(response.Headers.TryGetValues(AuthSecurityHeaders.CsrfToken, out var csrfHeaderValues));
        Assert.False(string.IsNullOrWhiteSpace(csrfHeaderValues!.SingleOrDefault()));
    }

    [Fact]
    public async Task Refresh_WithCookieOnly_ReturnsOk()
    {
        using var client = CreateClient();
        var (username, _, password) = await RegisterUserAsync(client);
        var cookieSettings = GetAuthCookieSettings();

        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
        {
            Username = username,
            Password = password
        });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var refreshCookie = ExtractCookie(loginResponse, cookieSettings.RefreshToken.Name);
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        refreshRequest.Headers.Add("Cookie", refreshCookie);
        refreshRequest.Headers.Add("Origin", "http://localhost:5173");

        var refreshResponse = await client.SendAsync(refreshRequest);

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var payload = await refreshResponse.Content.ReadFromJsonAsync<AuthSessionResponse>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.AccessToken));
        Assert.True(payload.ExpiresIn > 0);
        Assert.Equal("Bearer", payload.TokenType);
        Assert.True(refreshResponse.Headers.TryGetValues("Set-Cookie", out var refreshSetCookieValues));
        Assert.Contains(refreshSetCookieValues!, cookie => cookie.StartsWith(
            $"{cookieSettings.RefreshToken.Name}=",
            StringComparison.OrdinalIgnoreCase));
        Assert.True(refreshResponse.Headers.TryGetValues(AuthSecurityHeaders.CsrfToken, out var csrfHeaderValues));
        Assert.False(string.IsNullOrWhiteSpace(csrfHeaderValues!.SingleOrDefault()));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithAccessCookie_ReturnsSuccess()
    {
        var userId = $"cookie-user-{Guid.NewGuid():N}";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClient();
        var token = CreateJwt(userId);
        var cookieSettings = GetAuthCookieSettings();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/unread-count");
        request.Headers.Add("Cookie", $"{cookieSettings.AccessToken.Name}={token}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedPost_WithAccessCookieAndMissingCsrfHeader_IsRejected()
    {
        var userId = $"csrf-user-{Guid.NewGuid():N}";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClient();
        var token = CreateJwt(userId);
        var cookieSettings = GetAuthCookieSettings();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/communities")
        {
            Content = JsonContent.Create(new
            {
                Name = "CSRF Test Community",
                Slug = $"csrf-{Guid.NewGuid():N}",
                CreatedByUserId = userId
            })
        };
        request.Headers.Add("Cookie", $"{cookieSettings.AccessToken.Name}={token}");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task ProtectedPost_WithAccessCookieAndValidCsrfHeader_ReturnsSuccess()
    {
        var userId = $"csrf-ok-user-{Guid.NewGuid():N}";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClient();
        var token = CreateJwt(userId);
        var cookieSettings = GetAuthCookieSettings();
        var csrfToken = Guid.NewGuid().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/communities")
        {
            Content = JsonContent.Create(new
            {
                Name = "CSRF OK Community",
                Slug = $"csrf-ok-{Guid.NewGuid():N}",
                CreatedByUserId = "ignored-by-server"
            })
        };
        request.Headers.Add("Cookie",
            $"{cookieSettings.AccessToken.Name}={token}; {cookieSettings.CsrfToken.Name}={csrfToken}");
        request.Headers.Add(AuthSecurityHeaders.CsrfToken, csrfToken);
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithAccessCookieAndMissingCsrfHeader_IsRejected()
    {
        var userId = $"logout-csrf-user-{Guid.NewGuid():N}";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClient();
        var token = CreateJwt(userId);
        var cookieSettings = GetAuthCookieSettings();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", $"{cookieSettings.AccessToken.Name}={token}");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task CsrfEndpoint_WithAccessCookie_RotatesTokenAndReturnsHeader()
    {
        var userId = $"csrf-endpoint-user-{Guid.NewGuid():N}";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClient();
        var token = CreateJwt(userId);
        var cookieSettings = GetAuthCookieSettings();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/csrf");
        request.Headers.Add("Cookie", $"{cookieSettings.AccessToken.Name}={token}");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues(AuthSecurityHeaders.CsrfToken, out var csrfHeaderValues));
        var csrfHeaderToken = csrfHeaderValues!.SingleOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(csrfHeaderToken));

        var csrfCookie = ExtractCookie(response, cookieSettings.CsrfToken.Name);
        var cookieToken = csrfCookie[(csrfCookie.IndexOf('=') + 1)..];
        Assert.Equal(csrfHeaderToken, cookieToken);
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    private async Task<(string Username, string Email, string Password)> RegisterUserAsync(HttpClient client)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var email = $"{username}@example.com";
        var password = "Password123!";

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            Username = username,
            Email = email,
            Password = password,
            Name = "Test",
            LastName = "User"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var securitySettings = scope.ServiceProvider
            .GetRequiredService<IOptions<SecuritySettings>>()
            .Value;
        var autoVerify = configuration.GetValue<bool>("AuthSettings:AutoVerifyEmail");
        if (!autoVerify)
        {
            throw new Xunit.Sdk.XunitException("AuthSettings:AutoVerifyEmail is false in test configuration.");
        }
        var user = await dbContext.Users.FirstOrDefaultAsync(user => user.Username == username);
        if (user is null)
        {
            throw new Xunit.Sdk.XunitException("Registered user was not persisted.");
        }
        if (!user.EmailVerified)
        {
            throw new Xunit.Sdk.XunitException("Registered user email was not verified.");
        }
        if (user.PasswordUpdatedAt is null)
        {
            throw new Xunit.Sdk.XunitException("Registered user password update timestamp was not set.");
        }
        var pepper = configuration.GetValue<string>("SecuritySettings:Pepper") ?? string.Empty;
        if (!string.Equals(securitySettings.Pepper, pepper, StringComparison.Ordinal))
        {
            throw new Xunit.Sdk.XunitException("SecuritySettings:Pepper is not bound in AuthLibrary options.");
        }

        return (username, email, password);
    }

    private AuthCookieSettings GetAuthCookieSettings()
    {
        using var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider
            .GetRequiredService<IOptions<AuthCookieSettings>>()
            .Value;
    }

    private static bool HasCookieAttributes(string cookie, string cookieName, params string[] requiredAttributes)
    {
        if (!cookie.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var attribute in requiredAttributes)
        {
            if (!cookie.Contains(attribute, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string[] BuildExpectedCookieAttributes(
        AuthCookieDefinition cookie,
        bool requireSecure)
    {
        var attributes = new List<string>
        {
            $"Path={(string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path)}",
            $"SameSite={NormalizeSameSite(cookie.SameSite)}"
        };
        if (cookie.HttpOnly)
        {
            attributes.Add("HttpOnly");
        }
        if (requireSecure)
        {
            attributes.Add("Secure");
        }

        return attributes.ToArray();
    }

    private static string NormalizeSameSite(string? sameSite)
    {
        if (string.Equals(sameSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }
        if (string.Equals(sameSite, "strict", StringComparison.OrdinalIgnoreCase))
        {
            return "Strict";
        }

        return "Lax";
    }

    private static string ExtractCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieValues))
        {
            throw new Xunit.Sdk.XunitException("Expected Set-Cookie headers but none were returned.");
        }

        var matching = setCookieValues.FirstOrDefault(value =>
            value.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(matching))
        {
            throw new Xunit.Sdk.XunitException($"Expected cookie '{cookieName}' in Set-Cookie headers.");
        }

        var separator = matching.IndexOf(';');
        return separator < 0 ? matching : matching[..separator];
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!document.RootElement.TryGetProperty("error", out var errorElement))
        {
            return null;
        }

        return errorElement.TryGetProperty("code", out var codeElement)
            ? codeElement.GetString()
            : null;
    }

    private async Task SeedStandaloneUserAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var existing = await dbContext.Users.FindAsync(userId);
        if (existing is not null)
        {
            return;
        }

        dbContext.Users.Add(new User
        {
            Id = userId,
            Username = $"user-{userId}",
            Email = $"{userId}@example.com",
            Password = "hashed-password",
            Salt = "salt",
            EmailVerified = true,
            Name = "Cookie",
            LastName = "Tester",
            PasswordUpdatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();
    }

    private string CreateJwt(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        var signingKey = options.TokenValidationParameters.IssuerSigningKey
            ?? throw new InvalidOperationException("Jwt signing key missing.");
        var issuer = options.TokenValidationParameters.ValidIssuer;
        var audience = options.TokenValidationParameters.ValidAudience;
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record AuthSessionResponse(
        string AccessToken,
        int ExpiresIn,
        string TokenType,
        DateTime ExpiresAt,
        DateTime? RefreshTokenExpiresAt,
        AuthSessionUser? User);

    private sealed record AuthSessionUser(
        string Id,
        string Username,
        string Email,
        string? Name,
        string? LastName);
}
