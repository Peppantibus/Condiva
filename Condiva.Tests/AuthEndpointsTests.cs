using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Condiva.Tests.Infrastructure;
using AuthLibrary.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

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
}
