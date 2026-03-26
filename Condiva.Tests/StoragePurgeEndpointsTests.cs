using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Condiva.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Condiva.Tests;

public sealed class StoragePurgeEndpointsTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public StoragePurgeEndpointsTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PurgeAll_WithInvalidConfirmation_ReturnsBadRequest()
    {
        using var client = CreateClientWithToken("storage-purge-user");
        var response = await client.PostAsJsonAsync(
            "/api/storage/dev/purge-all",
            new PurgeRequest("WRONG_TOKEN"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PurgeAll_WithoutAuthorization_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.PostAsJsonAsync(
            "/api/storage/dev/purge-all",
            new PurgeRequest("PURGE_R2_BUCKET"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    private sealed record PurgeRequest(string Confirmation);
}
