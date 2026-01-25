using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Threading.Tasks;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Dtos;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Requests.Models;
using Condiva.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using RequestModel = Condiva.Api.Features.Requests.Models.Request;
using Condiva.Api.Features.Memberships.Models;

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
            Status = "Available"
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
            Status = "Open"
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
    public async Task CreateOffer_ItemOwnerMismatch_ReturnsBadRequest()
    {
        var ownerId = "offer-mismatch-owner";
        var offererId = "offer-mismatch-offerer";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, offererId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(offererId);
        var response = await client.PostAsJsonAsync("/api/offers", new
        {
            CommunityId = communityId,
            OffererUserId = offererId,
            RequestId = (string?)null,
            ItemId = itemId,
            Message = "Offer",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOffer_ItemOwnerMismatch_ReturnsBadRequest()
    {
        var offererId = "offer-update-mismatch-offerer";
        var otherOwnerId = "offer-update-mismatch-owner";
        var communityId = await SeedCommunityWithMembersAsync(offererId, otherOwnerId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var otherItemId = await SeedItemAsync(communityId, otherOwnerId);
        var offerId = await SeedOfferAsync(communityId, offererId, itemId, null);

        using var client = CreateClientWithToken(offererId);
        var response = await client.PutAsJsonAsync($"/api/offers/{offerId}", new
        {
            Id = offerId,
            CommunityId = communityId,
            OffererUserId = offererId,
            RequestId = (string?)null,
            ItemId = otherItemId,
            Message = "Updated",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    public async Task CreateLoan_StatusMustBeReserved_ReturnsBadRequest()
    {
        var lenderId = "loan-status-lender";
        var borrowerId = "loan-status-borrower";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsJsonAsync("/api/loans", new
        {
            CommunityId = communityId,
            ItemId = itemId,
            LenderUserId = lenderId,
            BorrowerUserId = borrowerId,
            RequestId = (string?)null,
            OfferId = (string?)null,
            Status = "Returned",
            StartAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(7)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLoan_ItemNotAvailable_ReturnsBadRequest()
    {
        var lenderId = "loan-unavailable-lender";
        var borrowerId = "loan-unavailable-borrower";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId, ItemStatus.Reserved);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsJsonAsync("/api/loans", new
        {
            CommunityId = communityId,
            ItemId = itemId,
            LenderUserId = lenderId,
            BorrowerUserId = borrowerId,
            RequestId = (string?)null,
            OfferId = (string?)null,
            Status = "Reserved",
            StartAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(7)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateLoan_LenderMustOwnItem_ReturnsBadRequest()
    {
        var managerId = "loan-owner-manager";
        var itemOwnerId = "loan-item-owner";
        var borrowerId = "loan-owner-borrower";
        var communityId = await SeedCommunityWithMembersAsync(managerId, itemOwnerId);
        await AddMemberAsync(communityId, borrowerId, MembershipRole.Member);
        var itemId = await SeedItemAsync(communityId, itemOwnerId);

        using var client = CreateClientWithToken(managerId);
        var response = await client.PostAsJsonAsync("/api/loans", new
        {
            CommunityId = communityId,
            ItemId = itemId,
            LenderUserId = managerId,
            BorrowerUserId = borrowerId,
            RequestId = (string?)null,
            OfferId = (string?)null,
            Status = "Reserved",
            StartAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(7)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLoan_NonManagerCannotChangeParticipants_ReturnsBadRequest()
    {
        var lenderId = "loan-update-participants-lender";
        var borrowerId = "loan-update-participants-borrower";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var loanId = await SeedLoanAsync(communityId, itemId, lenderId, borrowerId, null);

        using var client = CreateClientWithToken(borrowerId);
        var response = await client.PutAsJsonAsync($"/api/loans/{loanId}", new
        {
            Id = loanId,
            CommunityId = communityId,
            ItemId = itemId,
            LenderUserId = borrowerId,
            BorrowerUserId = borrowerId,
            RequestId = (string?)null,
            OfferId = (string?)null,
            Status = "Reserved",
            StartAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(7),
            ReturnedAt = (DateTime?)null
        });

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
            Status = "Open"
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
            Status = "Open"
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
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetCommunities_AsMember_ReturnsOnlyMemberCommunities()
    {
        var memberId = "community-list-member";
        var communityId = await SeedCommunityWithMembersAsync("community-list-owner", memberId);
        await SeedCommunityWithMembersAsync("community-list-owner-2");

        using var client = CreateClientWithToken(memberId);
        var response = await client.GetAsync("/api/communities");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<CommunityListItemDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload);
        Assert.Equal(communityId, payload![0].Id);
    }

    [Fact]
    public async Task CreateCommunity_IgnoresCreatedByUserId()
    {
        var creatorId = "community-creator";
        await SeedUserAsync(creatorId);

        using var client = CreateClientWithToken(creatorId);
        var response = await client.PostAsJsonAsync("/api/communities", new
        {
            Name = "My community",
            Slug = "my-community",
            Description = "Desc",
            CreatedByUserId = "spoofed-user"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CommunityDetailsDto>();
        Assert.NotNull(payload);
        Assert.Equal(creatorId, payload!.CreatedByUserId);
    }

    [Fact]
    public async Task GetItems_AsMember_ReturnsOnlyMemberCommunityItems()
    {
        var memberId = "items-list-member";
        var communityId = await SeedCommunityWithMembersAsync("items-list-owner", memberId);
        var otherCommunityId = await SeedCommunityWithMembersAsync("items-list-owner-2");
        var itemId = await SeedItemAsync(communityId, "items-list-owner");
        await SeedItemAsync(otherCommunityId, "items-list-owner-2");

        using var client = CreateClientWithToken(memberId);
        var response = await client.GetAsync("/api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<List<ItemListItemDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload);
        Assert.Equal(itemId, payload![0].Id);
    }

    [Fact]
    public async Task GetItem_AsNonMember_ReturnsBadRequest()
    {
        var ownerId = "item-read-owner";
        var nonMemberId = "item-read-non-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);
        await SeedUserAsync(nonMemberId);

        using var client = CreateClientWithToken(nonMemberId);
        var response = await client.GetAsync($"/api/items/{itemId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRequest_AsNonMember_ReturnsBadRequest()
    {
        var requesterId = "request-read-owner";
        var nonMemberId = "request-read-non-member";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        await SeedUserAsync(nonMemberId);

        using var client = CreateClientWithToken(nonMemberId);
        var response = await client.GetAsync($"/api/requests/{requestId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOffers_AsSuspendedMember_ReturnsBadRequest()
    {
        var requesterId = "request-offers-owner";
        var suspendedId = "request-offers-suspended";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        await AddMemberAsync(communityId, suspendedId, MembershipRole.Member, MembershipStatus.Suspended);
        var requestId = await SeedRequestAsync(communityId, requesterId);

        using var client = CreateClientWithToken(suspendedId);
        var response = await client.GetAsync($"/api/requests/{requestId}/offers");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOffer_AsNonMember_ReturnsBadRequest()
    {
        var offererId = "offer-read-offerer";
        var nonMemberId = "offer-read-non-member";
        var communityId = await SeedCommunityWithMembersAsync(offererId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var offerId = await SeedOfferAsync(communityId, offererId, itemId, null);
        await SeedUserAsync(nonMemberId);

        using var client = CreateClientWithToken(nonMemberId);
        var response = await client.GetAsync($"/api/offers/{offerId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLoan_AsNonMember_ReturnsBadRequest()
    {
        var lenderId = "loan-read-lender";
        var borrowerId = "loan-read-borrower";
        var nonMemberId = "loan-read-non-member";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, null);
        var loanId = await SeedLoanAsync(communityId, itemId, lenderId, borrowerId, offerId);
        await SeedUserAsync(nonMemberId);

        using var client = CreateClientWithToken(nonMemberId);
        var response = await client.GetAsync($"/api/loans/{loanId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetEvent_AsNonMember_ReturnsBadRequest()
    {
        var ownerId = "event-read-owner";
        var nonMemberId = "event-read-non-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var eventId = await SeedEventAsync(communityId, ownerId);
        await SeedUserAsync(nonMemberId);

        using var client = CreateClientWithToken(nonMemberId);
        var response = await client.GetAsync($"/api/events/{eventId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMembership_AsNonMember_ReturnsBadRequest()
    {
        var ownerId = "membership-read-owner";
        var nonMemberId = "membership-read-non-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var membershipId = await GetMembershipIdAsync(communityId, ownerId);
        await SeedUserAsync(nonMemberId);

        using var client = CreateClientWithToken(nonMemberId);
        var response = await client.GetAsync($"/api/memberships/{membershipId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEvent_AsNonModerator_ReturnsBadRequest()
    {
        var ownerId = "event-delete-owner";
        var memberId = "event-delete-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        var eventId = await SeedEventAsync(communityId, ownerId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.DeleteAsync($"/api/events/{eventId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCommunity_CreatesOwnerMembership()
    {
        var userId = "community-owner-create";
        await SeedUserAsync(userId);

        using var client = CreateClientWithToken(userId);
        var response = await client.PostAsJsonAsync("/api/communities", new
        {
            Name = "Owner community",
            Slug = "owner-community",
            Description = "Desc",
            CreatedByUserId = "ignored"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<CommunityDetailsDto>();
        Assert.NotNull(payload);

        var listResponse = await client.GetAsync("/api/communities");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<List<CommunityListItemDto>>();
        Assert.NotNull(listPayload);
        Assert.Contains(listPayload!, community => community.Id == payload!.Id);
    }

    [Fact]
    public async Task UpdateMembership_CannotChangeUserId()
    {
        var ownerId = "membership-update-owner";
        var memberId = "membership-update-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        var membershipId = await GetMembershipIdAsync(communityId, memberId);
        await SeedUserAsync("membership-update-other");

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PutAsJsonAsync($"/api/memberships/{membershipId}", new
        {
            UserId = "membership-update-other",
            CommunityId = communityId,
            Role = "Member",
            Status = "Active",
            InvitedByUserId = (string?)null,
            JoinedAt = DateTime.UtcNow
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMembership_CannotChangeCommunityId()
    {
        var ownerId = "membership-update-owner-2";
        var memberId = "membership-update-member-2";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        var otherCommunityId = await SeedCommunityWithMembersAsync(ownerId);
        var membershipId = await GetMembershipIdAsync(communityId, memberId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PutAsJsonAsync($"/api/memberships/{membershipId}", new
        {
            UserId = memberId,
            CommunityId = otherCommunityId,
            Role = "Member",
            Status = "Active",
            InvitedByUserId = (string?)null,
            JoinedAt = DateTime.UtcNow
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_ChangeCommunityId_ReturnsOk()
    {
        var ownerId = "item-update-owner";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var otherCommunityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PutAsJsonAsync($"/api/items/{itemId}", new
        {
            Id = itemId,
            CommunityId = otherCommunityId,
            OwnerUserId = ownerId,
            Name = "Updated item",
            Description = "Updated",
            Category = "Tools",
            Status = "Available"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRequest_ChangeCommunityId_ReturnsOk()
    {
        var requesterId = "request-update-owner";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        var otherCommunityId = await SeedCommunityWithMembersAsync(requesterId);
        var requestId = await SeedRequestAsync(communityId, requesterId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PutAsJsonAsync($"/api/requests/{requestId}", new
        {
            Id = requestId,
            CommunityId = otherCommunityId,
            RequesterUserId = requesterId,
            Title = "Updated request",
            Description = "Updated",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOffer_ChangeCommunityId_ReturnsOk()
    {
        var offererId = "offer-update-owner";
        var communityId = await SeedCommunityWithMembersAsync(offererId);
        var otherCommunityId = await SeedCommunityWithMembersAsync(offererId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var otherItemId = await SeedItemAsync(otherCommunityId, offererId);
        var offerId = await SeedOfferAsync(communityId, offererId, itemId, null);

        using var client = CreateClientWithToken(offererId);
        var response = await client.PutAsJsonAsync($"/api/offers/{offerId}", new
        {
            Id = offerId,
            CommunityId = otherCommunityId,
            OffererUserId = offererId,
            RequestId = (string?)null,
            ItemId = otherItemId,
            Message = "Updated",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateLoan_ChangeCommunityId_ReturnsOk()
    {
        var lenderId = "loan-update-lender";
        var borrowerId = "loan-update-borrower";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var otherCommunityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var otherItemId = await SeedItemAsync(otherCommunityId, lenderId);
        var loanId = await SeedLoanAsync(communityId, itemId, lenderId, borrowerId, null);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PutAsJsonAsync($"/api/loans/{loanId}", new
        {
            Id = loanId,
            CommunityId = otherCommunityId,
            ItemId = otherItemId,
            LenderUserId = lenderId,
            BorrowerUserId = borrowerId,
            RequestId = (string?)null,
            OfferId = (string?)null,
            Status = "Reserved",
            StartAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(7),
            ReturnedAt = (DateTime?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsMember_ReturnsBadRequest()
    {
        var ownerId = "event-create-owner";
        var memberId = "event-create-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsJsonAsync("/api/events", new
        {
            CommunityId = communityId,
            EntityType = "Test",
            EntityId = Guid.NewGuid().ToString(),
            Action = "Created",
            Payload = "{}"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_AsOwner_ReturnsCreated()
    {
        var ownerId = "event-create-owner-2";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PostAsJsonAsync("/api/events", new
        {
            CommunityId = communityId,
            EntityType = "Test",
            EntityId = Guid.NewGuid().ToString(),
            Action = "Created",
            Payload = "{}"
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
            await AddMemberAsync(communityId, memberId, MembershipRole.Member, MembershipStatus.Active, dbContext);
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
        MembershipStatus status = MembershipStatus.Active,
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
                Status = status,
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
            Status = status,
            CreatedAt = DateTime.UtcNow,
            JoinedAt = DateTime.UtcNow
        });
        await scopedContext.SaveChangesAsync();
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

    private async Task<string> SeedEventAsync(string communityId, string actorUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var evt = new Condiva.Api.Features.Events.Models.Event
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            ActorUserId = actorUserId,
            EntityType = "Test",
            EntityId = Guid.NewGuid().ToString(),
            Action = "Created",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Events.Add(evt);
        await dbContext.SaveChangesAsync();
        return evt.Id;
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
