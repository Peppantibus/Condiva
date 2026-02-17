using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Idempotency;
using Condiva.Api.Features.Communities.Dtos;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Loans.Dtos;
using Condiva.Api.Features.Offers.Dtos;
using Condiva.Api.Features.Requests.Dtos;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Features.Memberships.Dtos;
using Condiva.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using RequestModel = Condiva.Api.Features.Requests.Models.Request;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Reputations.Models;

namespace Condiva.Tests;

public sealed class RoleEnforcementTests : IClassFixture<CondivaApiFactory>
{
    private readonly CondivaApiFactory _factory;

    public RoleEnforcementTests(CondivaApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PutCommunity_AsNonOwner_ReturnsForbidden()
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("forbidden", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(payload.RootElement.GetProperty("traceId").GetString()));
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
    public async Task UpdateMembershipRole_AsNonOwner_ReturnsForbidden()
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    public async Task UpdateItem_AsNonOwner_ReturnsForbidden()
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRequest_AsNonRequester_ReturnsForbidden()
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateItem_IgnoresOwnerUserIdFromBody()
    {
        var actorUserId = "item-create-actor";
        var spoofedUserId = "item-create-spoofed";
        var communityId = await SeedCommunityWithMembersAsync(actorUserId, spoofedUserId);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.PostAsJsonAsync("/api/items", new
        {
            CommunityId = communityId,
            OwnerUserId = spoofedUserId,
            Name = "Actor item",
            Description = "Desc",
            Category = "Tools",
            Status = "Available"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ItemDetailsDto>();
        Assert.NotNull(payload);
        Assert.Equal(actorUserId, payload!.OwnerUserId);
    }

    [Fact]
    public async Task CreateRequest_IgnoresRequesterUserIdFromBody()
    {
        var actorUserId = "request-create-actor";
        var spoofedUserId = "request-create-spoofed";
        var communityId = await SeedCommunityWithMembersAsync(actorUserId, spoofedUserId);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.PostAsJsonAsync("/api/requests", new
        {
            CommunityId = communityId,
            RequesterUserId = spoofedUserId,
            Title = "Need drill",
            Description = "Desc",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RequestDetailsDto>();
        Assert.NotNull(payload);
        Assert.Equal(actorUserId, payload!.RequesterUserId);
    }

    [Fact]
    public async Task UpdateItem_CannotChangeOwnerUserId()
    {
        var ownerId = "item-owner-update-block";
        var otherMemberId = "item-owner-update-block-other";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, otherMemberId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.PutAsJsonAsync($"/api/items/{itemId}", new
        {
            Id = itemId,
            CommunityId = communityId,
            OwnerUserId = otherMemberId,
            Name = "Updated item",
            Description = "Updated",
            Category = "Tools",
            Status = "Available"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRequest_CannotChangeRequesterUserId()
    {
        var requesterId = "request-owner-update-block";
        var otherMemberId = "request-owner-update-block-other";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, otherMemberId);
        var requestId = await SeedRequestAsync(communityId, requesterId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PutAsJsonAsync($"/api/requests/{requestId}", new
        {
            Id = requestId,
            CommunityId = communityId,
            RequesterUserId = otherMemberId,
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
    public async Task WithdrawOffer_AsNonOfferer_ReturnsForbidden()
    {
        var requesterId = "requester-withdraw-user-2";
        var lenderId = "lender-withdraw-user-2";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsync($"/api/offers/{offerId}/withdraw", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WithdrawOffer_WhenRequestClosed_ReturnsConflict()
    {
        var requesterId = "requester-withdraw-user-3";
        var lenderId = "lender-withdraw-user-3";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);
        await SetRequestStatusAsync(requestId, RequestStatus.Closed);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsync($"/api/offers/{offerId}/withdraw", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RejectOffer_WhenRequestClosed_ReturnsConflict()
    {
        var requesterId = "requester-reject-user-3";
        var lenderId = "lender-reject-user-3";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);
        await SetRequestStatusAsync(requestId, RequestStatus.Closed);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsync($"/api/offers/{offerId}/reject", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AcceptOffer_AsRequester_ReturnsCreated()
    {
        var requesterId = "accept-requester-user";
        var lenderId = "accept-lender-user";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsJsonAsync($"/api/offers/{offerId}/accept", new
        {
            BorrowerUserId = requesterId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task AcceptOffer_IgnoresBorrowerUserIdFromBody()
    {
        var requesterId = "accept-requester-user-ignore";
        var lenderId = "accept-lender-user-ignore";
        var spoofedBorrowerId = "accept-spoofed-borrower-ignore";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        await AddMemberAsync(communityId, spoofedBorrowerId, MembershipRole.Member);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsJsonAsync($"/api/offers/{offerId}/accept", new
        {
            BorrowerUserId = spoofedBorrowerId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LoanDetailsDto>();
        Assert.NotNull(payload);
        Assert.Equal(requesterId, payload!.BorrowerUserId);
    }

    [Fact]
    public async Task AcceptOffer_AsNonRequester_ReturnsForbidden()
    {
        var requesterId = "accept-requester-user-2";
        var lenderId = "accept-lender-user-2";
        var memberId = "accept-other-user-2";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        await AddMemberAsync(communityId, memberId, MembershipRole.Member);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.PostAsJsonAsync($"/api/offers/{offerId}/accept", new
        {
            BorrowerUserId = memberId
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcceptOffer_WhenRequestClosed_ReturnsConflict()
    {
        var requesterId = "accept-requester-user-3";
        var lenderId = "accept-lender-user-3";
        var communityId = await SeedCommunityWithMembersAsync(requesterId, lenderId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, requestId);
        await SetRequestStatusAsync(requestId, RequestStatus.Closed);

        using var client = CreateClientWithToken(requesterId);
        var response = await client.PostAsJsonAsync($"/api/offers/{offerId}/accept", new
        {
            BorrowerUserId = requesterId
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AcceptOffer_WithoutRequest_ReturnsConflict()
    {
        var lenderId = "accept-no-request-lender";
        var communityId = await SeedCommunityWithMembersAsync(lenderId);
        var itemId = await SeedItemAsync(communityId, lenderId);
        var offerId = await SeedOfferAsync(communityId, lenderId, itemId, null);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsJsonAsync($"/api/offers/{offerId}/accept", new
        {
            BorrowerUserId = lenderId
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateOffer_IgnoresOffererUserIdFromBody()
    {
        var actorUserId = "offer-create-actor";
        var spoofedUserId = "offer-create-spoofed";
        var communityId = await SeedCommunityWithMembersAsync(actorUserId, spoofedUserId);
        var itemId = await SeedItemAsync(communityId, actorUserId);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.PostAsJsonAsync("/api/offers", new
        {
            CommunityId = communityId,
            OffererUserId = spoofedUserId,
            RequestId = (string?)null,
            ItemId = itemId,
            Message = "Offer",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OfferDetailsDto>();
        Assert.NotNull(payload);
        Assert.Equal(actorUserId, payload!.OffererUserId);
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
    public async Task UpdateOffer_CannotChangeOffererUserId()
    {
        var offererId = "offer-update-block-offerer";
        var otherMemberId = "offer-update-block-other";
        var communityId = await SeedCommunityWithMembersAsync(offererId, otherMemberId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var offerId = await SeedOfferAsync(communityId, offererId, itemId, null);

        using var client = CreateClientWithToken(offererId);
        var response = await client.PutAsJsonAsync($"/api/offers/{offerId}", new
        {
            Id = offerId,
            CommunityId = communityId,
            OffererUserId = otherMemberId,
            RequestId = (string?)null,
            ItemId = itemId,
            Message = "Updated",
            Status = "Open"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartLoan_AsUnrelatedMember_ReturnsForbidden()
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

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        var response = await client.PostAsync($"/api/loans/{loanId}/return-request", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReturnRequest_DoesNotChangeItemStatus_ReturnsOk()
    {
        var lenderId = "loan-return-request-lender";
        var borrowerId = "loan-return-request-borrower";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId, ItemStatus.InLoan);
        var loanId = await SeedLoanAsync(
            communityId,
            itemId,
            lenderId,
            borrowerId,
            null,
            Condiva.Api.Features.Loans.Models.LoanStatus.InLoan,
            ItemStatus.InLoan);

        using var client = CreateClientWithToken(borrowerId);
        var response = await client.PostAsync($"/api/loans/{loanId}/return-request", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var item = await dbContext.Items.FindAsync(itemId);
        Assert.NotNull(item);
        Assert.Equal(ItemStatus.InLoan, item!.Status);
    }

    [Fact]
    public async Task ReturnCancel_DoesNotChangeItemStatus_ReturnsOk()
    {
        var lenderId = "loan-return-cancel-lender";
        var borrowerId = "loan-return-cancel-borrower";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId, ItemStatus.InLoan);
        var loanId = await SeedLoanAsync(
            communityId,
            itemId,
            lenderId,
            borrowerId,
            null,
            Condiva.Api.Features.Loans.Models.LoanStatus.ReturnRequested,
            ItemStatus.InLoan);

        await SetLoanReturnRequestedAtAsync(loanId);

        using var client = CreateClientWithToken(borrowerId);
        var response = await client.PostAsync($"/api/loans/{loanId}/return-cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var item = await dbContext.Items.FindAsync(itemId);
        Assert.NotNull(item);
        Assert.Equal(ItemStatus.InLoan, item!.Status);
    }

    [Fact]
    public async Task ReturnConfirm_IgnoresItemStatus_ReturnsOk()
    {
        var lenderId = "loan-return-confirm-lender";
        var borrowerId = "loan-return-confirm-borrower";
        var communityId = await SeedCommunityWithMembersAsync(lenderId, borrowerId);
        var itemId = await SeedItemAsync(communityId, lenderId, ItemStatus.Available);
        var loanId = await SeedLoanAsync(
            communityId,
            itemId,
            lenderId,
            borrowerId,
            null,
            Condiva.Api.Features.Loans.Models.LoanStatus.ReturnRequested,
            ItemStatus.Available);

        await SetLoanReturnRequestedAtAsync(loanId);

        using var client = CreateClientWithToken(lenderId);
        var response = await client.PostAsync($"/api/loans/{loanId}/return-confirm", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();
        var loan = await dbContext.Loans.FindAsync(loanId);
        var item = await dbContext.Items.FindAsync(itemId);
        Assert.NotNull(loan);
        Assert.NotNull(item);
        Assert.Equal(Condiva.Api.Features.Loans.Models.LoanStatus.Returned, loan!.Status);
        Assert.NotNull(loan.ReturnedAt);
        Assert.NotNull(loan.ReturnConfirmedAt);
        Assert.Equal(ItemStatus.Available, item!.Status);
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

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
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
    public async Task JoinCommunity_SameIdempotencyKeySamePayload_ReplaysCreatedAndAvoidsDuplicates()
    {
        var ownerId = "join-idempotent-owner";
        var memberId = "join-idempotent-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        await SeedUserAsync(memberId);
        var enterCode = await GetEnterCodeAsync(communityId);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var client = CreateClientWithToken(memberId);
        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/communities/join")
        {
            Content = JsonContent.Create(new { EnterCode = enterCode })
        };
        firstRequest.Headers.Add(IdempotencyHeaders.Key, idempotencyKey);

        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/communities/join")
        {
            Content = JsonContent.Create(new { EnterCode = enterCode })
        };
        secondRequest.Headers.Add(IdempotencyHeaders.Key, idempotencyKey);

        var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.True(secondResponse.Headers.TryGetValues(IdempotencyHeaders.Replayed, out var replayValues));
        Assert.Equal("true", replayValues!.Single());

        var membershipCount = await GetMembershipCountAsync(communityId, memberId);
        Assert.Equal(1, membershipCount);
    }

    [Fact]
    public async Task JoinCommunity_SameIdempotencyKeyDifferentPayload_ReturnsConflict()
    {
        var ownerId = "join-idempotent-owner-conflict";
        var memberId = "join-idempotent-member-conflict";
        var firstCommunityId = await SeedCommunityWithMembersAsync(ownerId);
        var secondCommunityId = await SeedCommunityWithMembersAsync(ownerId);
        await SeedUserAsync(memberId);

        var firstEnterCode = await GetEnterCodeAsync(firstCommunityId);
        var secondEnterCode = await GetEnterCodeAsync(secondCommunityId);
        var idempotencyKey = Guid.NewGuid().ToString("N");

        using var client = CreateClientWithToken(memberId);
        var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/api/communities/join")
        {
            Content = JsonContent.Create(new { EnterCode = firstEnterCode })
        };
        firstRequest.Headers.Add(IdempotencyHeaders.Key, idempotencyKey);
        var firstResponse = await client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);

        var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/api/communities/join")
        {
            Content = JsonContent.Create(new { EnterCode = secondEnterCode })
        };
        secondRequest.Headers.Add(IdempotencyHeaders.Key, idempotencyKey);
        var secondResponse = await client.SendAsync(secondRequest);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        using var payload = await JsonDocument.ParseAsync(await secondResponse.Content.ReadAsStreamAsync());
        Assert.Equal("conflict", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task CommunityMembersEndpoint_AsMember_ReturnsPagedMembersWithReputation()
    {
        var ownerId = "members-endpoint-owner";
        var memberId = "members-endpoint-member";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        await SeedReputationAsync(communityId, memberId, 12, 4, 4, 3);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.GetAsync($"/api/communities/{communityId}/members?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<CommunityMemberListItemDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Page);
        Assert.Equal(10, payload.PageSize);
        Assert.Equal(2, payload.Total);

        var member = payload.Items.FirstOrDefault(item => item.UserId == memberId);
        Assert.NotNull(member);
        Assert.Equal(12, member!.ReputationSummary.Score);
        Assert.Equal(4, member.ReputationSummary.LendCount);
        Assert.Equal(4, member.ReputationSummary.ReturnCount);
        Assert.Equal(3, member.ReputationSummary.OnTimeReturnCount);
    }

    [Fact]
    public async Task CommunityMembersEndpoint_SupportsRoleStatusAndSearchFilters()
    {
        var ownerId = "members-filter-owner";
        var memberId = "members-filter-member";
        var moderatorId = "members-filter-moderator";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);
        await AddMemberAsync(communityId, moderatorId, MembershipRole.Moderator);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.GetAsync(
            $"/api/communities/{communityId}/members?role=Moderator&status=Active&search=moderator&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<CommunityMemberListItemDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Total);
        Assert.Single(payload.Items);
        Assert.Equal(moderatorId, payload.Items[0].UserId);
    }

    [Fact]
    public async Task CommunityMembersEndpoint_AsNonMember_ReturnsForbidden()
    {
        var ownerId = "members-non-member-owner";
        var outsiderId = "members-non-member-outsider";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        await SeedUserAsync(outsiderId);

        using var client = CreateClientWithToken(outsiderId);
        var response = await client.GetAsync($"/api/communities/{communityId}/members?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("forbidden", payload.RootElement.GetProperty("error").GetProperty("code").GetString());
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
        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<CommunityListItemDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(communityId, payload.Items[0].Id);
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
    public async Task GetMyCommunitiesContext_ReturnsCommunityAndMembershipContext()
    {
        var actorUserId = "my-communities-context-actor";
        var otherOwnerUserId = "my-communities-context-other-owner";
        var activeCommunityId = await SeedCommunityWithMembersAsync(actorUserId);
        var invitedCommunityId = await SeedCommunityWithMembersAsync(otherOwnerUserId);
        await AddMemberAsync(invitedCommunityId, actorUserId, MembershipRole.Moderator, MembershipStatus.Invited);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.GetAsync("/api/memberships/me/communities-context");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<MyCommunityContextListItemDto>>();
        Assert.NotNull(payload);

        var activeCommunity = payload!.Items.FirstOrDefault(item => item.CommunityId == activeCommunityId);
        Assert.NotNull(activeCommunity);
        Assert.Equal("Owner", activeCommunity!.Role);
        Assert.Equal("Active", activeCommunity.Status);
        Assert.Contains("manageMembers", activeCommunity.CommunityAllowedActions);
        Assert.Contains("leave", activeCommunity.MembershipAllowedActions);

        var invitedCommunity = payload.Items.FirstOrDefault(item => item.CommunityId == invitedCommunityId);
        Assert.NotNull(invitedCommunity);
        Assert.Equal("Moderator", invitedCommunity!.Role);
        Assert.Equal("Invited", invitedCommunity.Status);
        Assert.Single(invitedCommunity.CommunityAllowedActions);
        Assert.Equal("view", invitedCommunity.CommunityAllowedActions[0]);
        Assert.Single(invitedCommunity.MembershipAllowedActions);
        Assert.Equal("view", invitedCommunity.MembershipAllowedActions[0]);
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
        var response = await client.GetAsync($"/api/items?communityId={communityId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<ItemListItemDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(itemId, payload.Items[0].Id);
    }

    [Fact]
    public async Task GetItems_WithOwnerStatusSearchAndPaging_ReturnsFilteredPage()
    {
        var ownerId = "items-filter-owner";
        var actorUserId = "items-filter-actor";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, actorUserId);

        var actorAvailableItemId = await SeedItemAsync(communityId, actorUserId, ItemStatus.Available);
        await SeedItemAsync(communityId, actorUserId, ItemStatus.Reserved);
        await SeedItemAsync(communityId, ownerId, ItemStatus.Available);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.GetAsync(
            $"/api/items?communityId={communityId}&owner=me&status=Available&category=Tools&search=test&sort=name_asc&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<ItemListItemDto>>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Total);
        Assert.Single(payload.Items);
        Assert.Equal(actorAvailableItemId, payload.Items[0].Id);
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
    public async Task GetLoans_WithLentPerspective_ReturnsOnlyLoansAsLender()
    {
        var actorUserId = "loans-perspective-actor";
        var borrowerId = "loans-perspective-borrower";
        var otherLenderId = "loans-perspective-other-lender";
        var communityId = await SeedCommunityWithMembersAsync(actorUserId, borrowerId);
        await AddMemberAsync(communityId, otherLenderId, MembershipRole.Member);

        var itemLentId = await SeedItemAsync(communityId, actorUserId);
        var itemBorrowedId = await SeedItemAsync(communityId, otherLenderId);
        var lentLoanId = await SeedLoanAsync(communityId, itemLentId, actorUserId, borrowerId, null);
        var borrowedLoanId = await SeedLoanAsync(communityId, itemBorrowedId, otherLenderId, actorUserId, null);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.GetAsync(
            $"/api/loans?communityId={communityId}&perspective=lent&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<LoanListItemDto>>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, loan => loan.Id == lentLoanId);
        Assert.DoesNotContain(payload.Items, loan => loan.Id == borrowedLoanId);
    }

    [Fact]
    public async Task GetLoans_WithInvalidPerspective_ReturnsBadRequest()
    {
        var actorUserId = "loans-perspective-invalid-actor";
        var borrowerId = "loans-perspective-invalid-borrower";
        var communityId = await SeedCommunityWithMembersAsync(actorUserId, borrowerId);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.GetAsync(
            $"/api/loans?communityId={communityId}&perspective=invalid&page=1&pageSize=10");

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
        var listPayload = await listResponse.Content.ReadFromJsonAsync<PagedResponseDto<CommunityListItemDto>>();
        Assert.NotNull(listPayload);
        Assert.Contains(listPayload!.Items, community => community.Id == payload!.Id);
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
            Status = "Reserved"
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

    private async Task SetRequestStatusAsync(string requestId, RequestStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var request = await dbContext.Requests.FindAsync(requestId);
        if (request is null)
        {
            throw new InvalidOperationException("Request not found.");
        }

        request.Status = status;
        await dbContext.SaveChangesAsync();
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

    private async Task SetLoanReturnRequestedAtAsync(string loanId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var loan = await dbContext.Loans.FindAsync(loanId);
        if (loan is null)
        {
            throw new InvalidOperationException("Loan not found.");
        }

        loan.ReturnRequestedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
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

    private async Task<int> GetMembershipCountAsync(string communityId, string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        return await dbContext.Memberships.CountAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == userId);
    }

    private async Task SeedReputationAsync(
        string communityId,
        string userId,
        int score,
        int lendCount,
        int returnCount,
        int onTimeReturnCount)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var existing = await dbContext.Reputations.FirstOrDefaultAsync(reputation =>
            reputation.CommunityId == communityId
            && reputation.UserId == userId);
        if (existing is null)
        {
            dbContext.Reputations.Add(new ReputationProfile
            {
                Id = Guid.NewGuid().ToString(),
                CommunityId = communityId,
                UserId = userId,
                Score = score,
                LendCount = lendCount,
                ReturnCount = returnCount,
                OnTimeReturnCount = onTimeReturnCount,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Score = score;
            existing.LendCount = lendCount;
            existing.ReturnCount = returnCount;
            existing.OnTimeReturnCount = onTimeReturnCount;
            existing.UpdatedAt = DateTime.UtcNow;
        }

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
