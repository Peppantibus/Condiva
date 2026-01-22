using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Requests.Models;
using Condiva.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using RequestModel = Condiva.Api.Features.Requests.Models.Request;

namespace Condiva.Tests;

public sealed class RoleEnforcementTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public RoleEnforcementTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PutCommunity_AsNonOwner_ReturnsBadRequest()
    {
        var ownerId = "owner-user";
        var memberId = "member-user";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PutAsJsonAsync($"/api/communities/{communityId}", new
        {
            Id = communityId,
            Name = "Updated",
            Slug = "updated",
            CreatedByUserId = ownerId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMembershipRole_AsOwner_UpdatesRole()
    {
        var ownerId = "owner-role-user";
        var memberId = "member-role-user";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        var membershipId = await GetMembershipIdAsync(communityId, memberId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PostAsJsonAsync($"/api/memberships/{membershipId}/role", new
        {
            Role = "Moderator"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMembershipRole_AsNonOwner_ReturnsBadRequest()
    {
        var ownerId = "owner-role-user-2";
        var memberId = "member-role-user-2";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        var membershipId = await GetMembershipIdAsync(communityId, memberId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsJsonAsync($"/api/memberships/{membershipId}/role", new
        {
            Role = "Moderator"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LeaveCommunity_AsLastOwner_ReturnsBadRequest()
    {
        var ownerId = "owner-only-user";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PostAsync($"/api/memberships/leave/{communityId}", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_AsNonOwner_ReturnsBadRequest()
    {
        var ownerId = "item-owner-user";
        var memberId = "item-member-user";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PutAsJsonAsync($"/api/items/{itemId}", new
        {
            Id = itemId,
            CommunityId = communityId,
            OwnerUserId = ownerId,
            Name = "Updated item",
            Description = "Updated",
            Category = "Tools",
            Status = ItemStatus.Available
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRequest_AsNonRequester_ReturnsBadRequest()
    {
        var requesterId = "requester-user";
        var memberId = "requester-member-user";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, memberId);
        var requestId = await SeedRequestAsync(communityId, requesterId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PutAsJsonAsync($"/api/requests/{requestId}", new
        {
            Id = requestId,
            CommunityId = communityId,
            RequesterUserId = requesterId,
            Title = "Updated request",
            Description = "Updated",
            Status = RequestStatus.Open
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMembership_AsLastOwner_ReturnsBadRequest()
    {
        var ownerId = "owner-delete-user";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var membershipId = await GetMembershipIdAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.DeleteAsync($"/api/memberships/{membershipId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RejectOffer_AsRequestOwner_ReturnsOk()
    {
        var requesterId = "requester-offer-user";
        var lenderId = "lender-offer-user";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsync($"/api/offers/{offerId}/reject", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectOffer_AsUnrelatedMember_ReturnsBadRequest()
    {
        var requesterId = "requester-offer-user-2";
        var lenderId = "lender-offer-user-2";
        var memberId = "other-offer-user-2";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        await AddMemberAsync(communityId, memberId, MembershipRole.Member);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsync($"/api/offers/{offerId}/reject", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task WithdrawOffer_AsOfferer_ReturnsOk()
    {
        var requesterId = "requester-withdraw-user";
        var lenderId = "lender-withdraw-user";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsync($"/api/offers/{offerId}/withdraw", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task StartLoan_AsUnrelatedMember_ReturnsBadRequest()
    {
        var lenderId = "loan-lender-user";
        var borrowerId = "loan-borrower-user";
        var memberId = "loan-other-user";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        await AddMemberAsync(communityId, memberId, MembershipRole.Member);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, null);
        var loanId = await SeedLoanAsync(communityId, itemId, lenderId, borrowerId, offerId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsync($"/api/loans/{loanId}/start", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnLoan_AsUnrelatedMember_ReturnsBadRequest()
    {
        var lenderId = "loan-return-lender-user";
        var borrowerId = "loan-return-borrower-user";
        var memberId = "loan-return-other-user";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        await AddMemberAsync(communityId, memberId, MembershipRole.Member);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, null);
        var loanId = await SeedLoanAsync(
            communityId,
            itemId,
            lenderId,
            borrowerId,
            offerId,
            Condiva.Api.Features.Loans.Models.LoanStatus.InLoan,
            ItemStatus.InLoan);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsync($"/api/loans/{loanId}/return", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RejectOffer_WithoutRequest_AsOfferer_ReturnsOk()
    {
        var lenderId = "offer-no-request-lender";
        var communityId = await SeedCommunityWithMembersAsync(lenderId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, null);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsync($"/api/offers/{offerId}/reject", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectOffer_WithoutRequest_AsUnrelatedMember_ReturnsBadRequest()
    {
        var lenderId = "offer-no-request-lender-2";
        var memberId = "offer-no-request-member-2";
        var communityId = await SeedCommunityWithMembersAsync(lenderId);
        await AddMemberAsync(communityId, memberId, MembershipRole.Member);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, null);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsync($"/api/offers/{offerId}/reject", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task JoinCommunity_WithExpiredEnterCode_ReturnsBadRequest()
    {
        var ownerId = "join-expired-owner";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        await ExpireEnterCodeAsync(communityId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PostAsJsonAsync("/api/communities/join", new
        {
            EnterCode = await GetEnterCodeAsync(communityId)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task JoinCommunity_WithValidEnterCode_ReturnsCreated()
    {
        var ownerId = "join-valid-owner";
        var memberId = "join-valid-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        await SeedUserAsync(memberId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsJsonAsync("/api/communities/join", new
        {
            EnterCode = await GetEnterCodeAsync(communityId)
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_AboveDailyLimit_ReturnsBadRequest()
    {
        var requesterId = "limit-requester";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        var requestIds = new[]
        {
            await SeedRequestAsync(communityId, requesterId),
            await SeedRequestAsync(communityId, requesterId),
            await SeedRequestAsync(communityId, requesterId)
        };

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsJsonAsync("/api/requests", new
        {
            CommunityId = communityId,
            RequesterUserId = requesterId,
            Title = "Need item",
            Description = "Desc",
            Status = RequestStatus.Open
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_DuplicateWithinWindow_ReturnsBadRequest()
    {
        var requesterId = "dup-requester";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        dbContext.Requests.Add(new RequestModel
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            RequesterUserId = requesterId,
            Title = "Need item",
            Description = "Desc",
            Status = RequestStatus.Open,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await dbContext.SaveChangesAsync();

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsJsonAsync("/api/requests", new
        {
            CommunityId = communityId,
            RequesterUserId = requesterId,
            Title = "Need item",
            Description = "Desc",
            Status = RequestStatus.Open
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRequest_SameContentDifferentCommunity_ReturnsCreated()
    {
        var requesterId = "dup-requester-cross";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        var otherCommunityId = await SeedCommunityWithMembersAsync(requesterId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsJsonAsync("/api/requests", new
        {
            CommunityId = otherCommunityId,
            RequesterUserId = requesterId,
            Title = "Need item",
            Description = "Desc",
            Status = RequestStatus.Open
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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
            await AddMemberAsync(communityId, memberId, MembershipRole.Member, dbContext);
        }

        await dbContext.SaveChangesAsync();
        return communityId;
    }

    private async Task<string> GetEnterCodeAsync(string communityId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var community = await dbContext.Communities.FindAsync(communityId);
        if (community is null)
        {
            throw new InvalidOperationException("Community not found.");
        }

        return community.EnterCode;
    }

    private async Task ExpireEnterCodeAsync(string communityId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var community = await dbContext.Communities.FindAsync(communityId);
        if (community is null)
        {
            throw new InvalidOperationException("Community not found.");
        }

        community.EnterCodeExpiresAt = DateTime.UtcNow.AddDays(-1);
        await dbContext.SaveChangesAsync();
    }

    private async Task AddMemberAsync(
        string communityId,
        string userId,
        MembershipRole role,
        CondivaDbContext? dbContext = null)
    {
        await SeedUserAsync(userId);

        if (dbContext is not null)
        {
            dbContext.Memberships.Add(new Membership
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                CommunityId = communityId,
                Role = role,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow,
                JoinedAt = DateTime.UtcNow
            });
            return;
        }

        using var scope = _factory.Services.CreateScope();
        var scopedContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        scopedContext.Memberships.Add(new Membership
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            CommunityId = communityId,
            Role = role,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            JoinedAt = DateTime.UtcNow
        });
        await scopedContext.SaveChangesAsync();
    }

    private async Task<string> SeedItemAsync(string communityId, string ownerId)
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
            Status = ItemStatus.Available,
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

        var request = new RequestModel
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

        var offer = new Condiva.Api.Features.Offers.Models.Offer
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            OffererUserId = offererUserId,
            RequestId = requestId,
            ItemId = itemId,
            Message = "Offer",
            Status = Condiva.Api.Features.Offers.Models.OfferStatus.Open,
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Offers.Add(offer);
        await dbContext.SaveChangesAsync();
        return offer.Id;
    }

    private async Task<string> SeedLoanAsync(
        string communityId,
        string itemId,
        string lenderUserId,
        string borrowerUserId,
        string? offerId,
        Condiva.Api.Features.Loans.Models.LoanStatus status = Condiva.Api.Features.Loans.Models.LoanStatus.Reserved,
        ItemStatus? itemStatus = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var loan = new Condiva.Api.Features.Loans.Models.Loan
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            ItemId = itemId,
            LenderUserId = lenderUserId,
            BorrowerUserId = borrowerUserId,
            OfferId = offerId,
            Status = status,
            StartAt = DateTime.UtcNow
        };
        dbContext.Loans.Add(loan);
        if (itemStatus is not null)
        {
            var item = await dbContext.Items.FindAsync(itemId);
            if (item is not null)
            {
                item.Status = itemStatus.Value;
            }
        }
        await dbContext.SaveChangesAsync();
        return loan.Id;
    }

    private async Task<string> GetMembershipIdAsync(string communityId, string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var membership = await dbContext.Memberships.FirstOrDefaultAsync(m =>
            m.CommunityId == communityId && m.UserId == userId);
        if (membership is null)
        {
            throw new InvalidOperationException("Membership not found.");
        }

        return membership.Id;
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
