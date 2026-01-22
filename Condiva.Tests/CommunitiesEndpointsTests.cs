using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Condiva.Tests;

public sealed class CommunitiesEndpointsTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public CommunitiesEndpointsTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostCommunities_WithoutAuth_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync("/api/communities", new
        {
            Name = "Test community",
            Slug = "test-community",
            CreatedByUserId = "user-1"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostCommunities_WithValidTokenAndExistingUser_ReturnsCreated()
    {
        var userId = "user-1";
        await SeedUserAsync(userId);

        using var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });
        var token = CreateJwt(userId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/communities", new
        {
            Name = "Test community",
            Slug = "test-community",
            CreatedByUserId = userId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<Community>();
        Assert.NotNull(payload);
        Assert.Equal(userId, payload!.CreatedByUserId);
        Assert.False(string.IsNullOrWhiteSpace(payload.Id));
        Assert.True(payload.CreatedAt > DateTime.MinValue);
    }

    private async Task SeedUserAsync(string userId)
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
            Username = "testuser",
            Email = "test@example.com",
            Password = "hashed-password",
            Salt = "salt",
            EmailVerified = true,
            Name = "Test",
            LastName = "User"
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
}
