using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Offers.Dtos;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Dtos;
using Condiva.Api.Features.Requests.Models;
using Condiva.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Condiva.Tests;

public sealed class ApiPayloadTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public ApiPayloadTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetItems_WithoutCommunityId_ReturnsBadRequest()
    {
        var userId = $"items-user-{Guid.NewGuid():N}";
        await SeedUserAsync(userId);
        using var client = CreateClientWithToken(userId);

        var response = await client.GetAsync("/api/items");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorEnvelopeAsync(response, "validation_error", hasFields: true, expectedField: "communityId");
    }

    [Fact]
    public async Task GetItem_NotFound_ReturnsNotFoundErrorEnvelope()
    {
        var userId = $"items-notfound-{Guid.NewGuid():N}";
        await SeedUserAsync(userId);
        using var client = CreateClientWithToken(userId);

        var response = await client.GetAsync($"/api/items/{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorEnvelopeAsync(response, "not_found");
    }

    [Fact]
    public async Task GetItems_ReturnsOwnerSummary()
    {
        var ownerId = $"items-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.GetAsync($"/api/items?communityId={communityId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<ItemListItemDto>>();
        Assert.NotNull(payload);

        var item = payload!.Find(entry => entry.Id == itemId);
        Assert.NotNull(item);
        Assert.NotNull(item!.Owner);
        Assert.Equal(ownerId, item.Owner.Id);
        Assert.False(string.IsNullOrWhiteSpace(item.Owner.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(item.Owner.UserName));
        Assert.NotNull(item.AllowedActions);
        Assert.Contains("view", item.AllowedActions!);
        Assert.Contains("update", item.AllowedActions!);
    }

    [Fact]
    public async Task GetRequests_WithoutCommunityId_ReturnsBadRequest()
    {
        var userId = $"requests-user-{Guid.NewGuid():N}";
        await SeedUserAsync(userId);
        using var client = CreateClientWithToken(userId);

        var response = await client.GetAsync("/api/requests");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRequests_ReturnsOwnerAndCommunitySummary()
    {
        var requesterId = $"requests-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        var requestId = await SeedRequestAsync(communityId, requesterId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.GetAsync($"/api/requests?communityId={communityId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<RequestListItemDto>>();
        Assert.NotNull(payload);

        var request = payload!.Find(entry => entry.Id == requestId);
        Assert.NotNull(request);
        Assert.NotNull(request!.Owner);
        Assert.Equal(requesterId, request.Owner.Id);
        Assert.False(string.IsNullOrWhiteSpace(request.Owner.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(request.Owner.UserName));
        Assert.NotNull(request.Community);
        Assert.Equal(communityId, request.Community.Id);
        Assert.False(string.IsNullOrWhiteSpace(request.Community.Name));
        Assert.False(string.IsNullOrWhiteSpace(request.Community.Slug));
        Assert.NotNull(request.AllowedActions);
        Assert.Contains("view", request.AllowedActions!);
        Assert.Contains("update", request.AllowedActions!);
    }

    [Fact]
    public async Task GetRequestsMe_IncludesCommunitySummary()
    {
        var requesterId = $"requests-me-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        var otherCommunityId = await SeedCommunityWithMembersAsync(requesterId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var otherRequestId = await SeedRequestAsync(otherCommunityId, requesterId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.GetAsync("/api/requests/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<RequestListItemDto>>();
        Assert.NotNull(payload);

        Assert.Contains(payload!.Items, item => item.Id == requestId);
        Assert.Contains(payload.Items, item => item.Id == otherRequestId);
        Assert.All(payload.Items, item =>
        {
            Assert.NotNull(item.Community);
            Assert.False(string.IsNullOrWhiteSpace(item.Community.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Community.Name));
            Assert.False(string.IsNullOrWhiteSpace(item.Community.Slug));
            Assert.NotNull(item.AllowedActions);
            Assert.Contains("view", item.AllowedActions!);
        });
    }

    [Fact]
    public async Task GetOffers_ReturnsCommunitySummary()
    {
        var offererId = $"offers-user-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(offererId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var offerId = await SeedOfferAsync(communityId, offererId, itemId, null);

        using var client = CreateClientWithToken(offererId);
        var response = await client.GetAsync("/api/offers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<OfferListItemDto>>();
        Assert.NotNull(payload);

        var offer = payload!.Find(entry => entry.Id == offerId);
        Assert.NotNull(offer);
        Assert.NotNull(offer!.Community);
        Assert.Equal(communityId, offer.Community.Id);
        Assert.False(string.IsNullOrWhiteSpace(offer.Community.Name));
        Assert.False(string.IsNullOrWhiteSpace(offer.Community.Slug));
        Assert.NotNull(offer.Offerer);
        Assert.False(string.IsNullOrWhiteSpace(offer.Offerer.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(offer.Offerer.UserName));
        Assert.NotNull(offer.AllowedActions);
        Assert.Contains("view", offer.AllowedActions!);
        Assert.Contains("withdraw", offer.AllowedActions!);
    }

    [Fact]
    public async Task GetRequestOffers_ReturnsCommunitySummary()
    {
        var requesterId = $"offer-requester-{Guid.NewGuid():N}";
        var offererId = $"offerer-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, offererId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var offerId = await SeedOfferAsync(communityId, offererId, itemId, requestId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.GetAsync($"/api/requests/{requestId}/offers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<OfferListItemDto>>();
        Assert.NotNull(payload);

        var offer = payload!.Items.FirstOrDefault(entry => entry.Id == offerId);
        Assert.NotNull(offer);
        Assert.NotNull(offer!.Community);
        Assert.Equal(communityId, offer.Community.Id);
        Assert.False(string.IsNullOrWhiteSpace(offer.Community.Name));
        Assert.False(string.IsNullOrWhiteSpace(offer.Community.Slug));
        Assert.NotNull(offer.AllowedActions);
        Assert.Contains("view", offer.AllowedActions!);
        Assert.Contains("accept", offer.AllowedActions!);
        Assert.Contains("reject", offer.AllowedActions!);
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
            Username = $"{userId}-name",
            Email = $"{userId}@example.com",
            Password = "hashed-password",
            Salt = "salt",
            EmailVerified = true,
            Name = "Test",
            LastName = "User"
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<string> SeedCommunityWithMembersAsync(string ownerId, string? memberId = null)
    {
        await SeedUserAsync(ownerId);
        if (!string.IsNullOrWhiteSpace(memberId))
        {
            await SeedUserAsync(memberId);
        }

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var communityId = Guid.NewGuid().ToString();
        var community = new Community
        {
            Id = communityId,
            Name = "Test Community",
            Slug = $"test-{communityId}",
            CreatedByUserId = ownerId,
            EnterCode = Guid.NewGuid().ToString("N"),
            EnterCodeExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Communities.Add(community);

        dbContext.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid().ToString(),
            UserId = ownerId,
            CommunityId = communityId,
            Role = MembershipRole.Owner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            JoinedAt = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(memberId))
        {
            dbContext.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid().ToString(),
                UserId = memberId,
                CommunityId = communityId,
                Role = MembershipRole.Member,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow,
                JoinedAt = DateTime.UtcNow
            });
        }

        await dbContext.SaveChangesAsync();
        return communityId;
    }

    private async Task<string> SeedItemAsync(
        string communityId,
        string ownerId,
        ItemStatus status = ItemStatus.Available)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var item = new Item
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            OwnerUserId = ownerId,
            Name = "Test item",
            Description = "Desc",
            Category = "Tools",
            Status = status,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Items.Add(item);
        await dbContext.SaveChangesAsync();
        return item.Id;
    }

    private async Task<string> SeedRequestAsync(string communityId, string requesterId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var request = new Request
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            RequesterUserId = requesterId,
            Title = "Need item",
            Description = "Desc",
            Status = RequestStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Requests.Add(request);
        await dbContext.SaveChangesAsync();
        return request.Id;
    }

    private async Task<string> SeedOfferAsync(
        string communityId,
        string offererUserId,
        string itemId,
        string? requestId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var offer = new Offer
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            OffererUserId = offererUserId,
            RequestId = requestId,
            ItemId = itemId,
            Message = "Offer",
            Status = OfferStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Offers.Add(offer);
        await dbContext.SaveChangesAsync();
        return offer.Id;
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

    private static async Task AssertErrorEnvelopeAsync(
        HttpResponseMessage response,
        string expectedCode,
        bool hasFields = false,
        string? expectedField = null)
    {
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

        Assert.True(payload.RootElement.TryGetProperty("error", out var errorNode));
        Assert.True(errorNode.TryGetProperty("code", out var codeNode));
        Assert.Equal(expectedCode, codeNode.GetString());
        Assert.True(errorNode.TryGetProperty("message", out var messageNode));
        Assert.False(string.IsNullOrWhiteSpace(messageNode.GetString()));
        Assert.True(payload.RootElement.TryGetProperty("traceId", out var traceIdNode));
        Assert.False(string.IsNullOrWhiteSpace(traceIdNode.GetString()));

        if (hasFields)
        {
            Assert.True(errorNode.TryGetProperty("fields", out var fieldsNode));
            Assert.Equal(JsonValueKind.Object, fieldsNode.ValueKind);
            if (!string.IsNullOrWhiteSpace(expectedField))
            {
                Assert.True(fieldsNode.TryGetProperty(expectedField, out _));
            }
        }
    }
}
