using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Condiva.Api.Common.Auth.Models;
using Condiva.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Condiva.Tests;

public sealed class StorageResolveEndpointsTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public StorageResolveEndpointsTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResolveBatch_WithValidKeys_ReturnsSignedUrls()
    {
        var userId = "storage-resolve-user";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClientWithToken(userId);
        var request = new ResolveRequest(new[]
        {
            "items/abc/image.png",
            "items/abc/image.png",
            "requests/def/photo.jpg"
        });

        var response = await client.PostAsJsonAsync("/api/storage/resolve", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ResolveResponse>();
        Assert.NotNull(payload);
        Assert.Equal(300, payload!.ExpiresIn);
        Assert.Equal(2, payload.Items.Count);
        Assert.Contains(payload.Items, item => item.ObjectKey == "items/abc/image.png");
        Assert.Contains(payload.Items, item => item.ObjectKey == "requests/def/photo.jpg");
        Assert.All(payload.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.DownloadUrl)));
    }

    [Fact]
    public async Task ResolveBatch_WithInvalidKey_ReturnsBadRequest()
    {
        var userId = "storage-resolve-invalid";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClientWithToken(userId);
        var response = await client.PostAsJsonAsync("/api/storage/resolve", new ResolveRequest(new[]
        {
            "../outside/path.png"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResolveBatch_WithEmptyKeys_ReturnsBadRequest()
    {
        var userId = "storage-resolve-empty";
        await SeedStandaloneUserAsync(userId);

        using var client = CreateClientWithToken(userId);
        var response = await client.PostAsJsonAsync("/api/storage/resolve", new ResolveRequest(Array.Empty<string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateClientWithToken(string userId)
    {
        var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        var token = CreateJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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
            Username = $"{userId}-name",
            Email = $"{userId}@example.com",
            Password = "hashed-password",
            Salt = "salt",
            EmailVerified = true,
            Name = "Storage",
            LastName = "Tester"
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

    private sealed record ResolveRequest(IReadOnlyList<string> ObjectKeys);

    private sealed record ResolveResponse(IReadOnlyList<ResolveItem> Items, int ExpiresIn);

    private sealed record ResolveItem(string ObjectKey, string DownloadUrl);
}
