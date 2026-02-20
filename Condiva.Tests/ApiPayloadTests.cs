using System;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Features.Communities.Dtos;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Dashboard.Dtos;
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
    public async Task GetItem_ReturnsEtagHeader()
    {
        var ownerId = $"item-etag-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.GetAsync($"/api/items/{itemId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("ETag", out var etagValues));
        Assert.False(string.IsNullOrWhiteSpace(etagValues!.Single()));
    }

    [Fact]
    public async Task PutItem_WithStaleIfMatch_ReturnsPreconditionFailed()
    {
        var ownerId = $"item-stale-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/items/{itemId}")
        {
            Content = JsonContent.Create(new
            {
                CommunityId = communityId,
                OwnerUserId = ownerId,
                Name = "Updated item",
                Description = "Updated",
                Category = "Tools",
                Status = "Available"
            })
        };
        request.Headers.TryAddWithoutValidation("If-Match", "\"stale-version\"");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        await AssertErrorEnvelopeAsync(response, "precondition_failed");
    }

    [Fact]
    public async Task DeleteItem_WithStaleIfMatch_ReturnsPreconditionFailed()
    {
        var ownerId = $"item-delete-stale-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/items/{itemId}");
        request.Headers.TryAddWithoutValidation("If-Match", "\"stale-version\"");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        await AssertErrorEnvelopeAsync(response, "precondition_failed");
    }

    [Fact]
    public async Task PutItem_WithCurrentIfMatch_UpdatesAndReturnsNewEtag()
    {
        var ownerId = $"item-update-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var getResponse = await client.GetAsync($"/api/items/{itemId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var currentEtag = GetRequiredEtag(getResponse);

        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/items/{itemId}")
        {
            Content = JsonContent.Create(new
            {
                CommunityId = communityId,
                OwnerUserId = ownerId,
                Name = "Updated item name",
                Description = "Updated item description",
                Category = "Tools",
                Status = "Available"
            })
        };
        request.Headers.TryAddWithoutValidation("If-Match", currentEtag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ItemDetailsDto>();
        Assert.NotNull(payload);
        Assert.Equal("Updated item name", payload!.Name);

        var updatedEtag = GetRequiredEtag(response);
        Assert.NotEqual(currentEtag, updatedEtag);
    }

    [Fact]
    public async Task DeleteItem_WithCurrentIfMatch_DeletesResource()
    {
        var ownerId = $"item-delete-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);
        var getResponse = await client.GetAsync($"/api/items/{itemId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var currentEtag = GetRequiredEtag(getResponse);

        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/items/{itemId}");
        deleteRequest.Headers.TryAddWithoutValidation("If-Match", currentEtag);
        var deleteResponse = await client.SendAsync(deleteRequest);

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var readAfterDeleteResponse = await client.GetAsync($"/api/items/{itemId}");
        Assert.Equal(HttpStatusCode.NotFound, readAfterDeleteResponse.StatusCode);
        await AssertErrorEnvelopeAsync(readAfterDeleteResponse, "not_found");
    }

    [Fact]
    public async Task ListEndpoints_ReturnUniformPagedEnvelopeShape()
    {
        var ownerId = $"paged-owner-{Guid.NewGuid():N}";
        var offererId = $"paged-offerer-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, offererId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var requestId = await SeedRequestAsync(communityId, ownerId);
        await SeedOfferAsync(communityId, offererId, itemId, requestId);

        using var client = CreateClientWithToken(ownerId);

        var itemsResponse = await client.GetAsync($"/api/items?communityId={communityId}");
        await AssertPagedEnvelopeAsync(itemsResponse, "createdAt", "desc");

        var requestsResponse = await client.GetAsync($"/api/requests?communityId={communityId}");
        await AssertPagedEnvelopeAsync(requestsResponse, "createdAt", "desc");

        var offersResponse = await client.GetAsync("/api/offers");
        await AssertPagedEnvelopeAsync(offersResponse, "createdAt", "desc");

        var communitiesResponse = await client.GetAsync("/api/communities");
        await AssertPagedEnvelopeAsync(communitiesResponse, "name", "asc");
    }

    [Fact]
    public async Task ItemDates_AreSerializedAsUtcIso8601()
    {
        var ownerId = $"utc-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        var itemId = await SeedItemAsync(communityId, ownerId);

        using var client = CreateClientWithToken(ownerId);

        var listResponse = await client.GetAsync($"/api/items?communityId={communityId}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using var listPayload = await JsonDocument.ParseAsync(await listResponse.Content.ReadAsStreamAsync());
        var listCreatedAt = listPayload.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .First(item => item.GetProperty("id").GetString() == itemId)
            .GetProperty("createdAt")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(listCreatedAt));
        Assert.EndsWith("Z", listCreatedAt!, StringComparison.Ordinal);

        var detailResponse = await client.GetAsync($"/api/items/{itemId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        using var detailPayload = await JsonDocument.ParseAsync(await detailResponse.Content.ReadAsStreamAsync());
        var detailCreatedAt = detailPayload.RootElement.GetProperty("createdAt").GetString();
        Assert.False(string.IsNullOrWhiteSpace(detailCreatedAt));
        Assert.EndsWith("Z", detailCreatedAt!, StringComparison.Ordinal);
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

        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<ItemListItemDto>>();
        Assert.NotNull(payload);

        var item = payload!.Items.FirstOrDefault(entry => entry.Id == itemId);
        Assert.NotNull(item);
        Assert.NotNull(item!.Owner);
        Assert.Equal(ownerId, item.Owner.Id);
        Assert.False(string.IsNullOrWhiteSpace(item.Owner.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(item.Owner.UserName));
        Assert.NotNull(item.AllowedActions);
        Assert.Contains("view", item.AllowedActions!);
        Assert.Contains("update", item.AllowedActions!);
        Assert.Equal("createdAt", payload.Sort);
        Assert.Equal("desc", payload.Order);
    }

    [Fact]
    public async Task UserSummaries_WithProfileImageKey_IncludeAvatarUrl()
    {
        var requesterId = $"avatar-requester-{Guid.NewGuid():N}";
        var offererId = $"avatar-offerer-{Guid.NewGuid():N}";
        await SeedUserAsync(requesterId, $"users/{requesterId}/avatar.png");
        await SeedUserAsync(offererId, $"users/{offererId}/avatar.png");

        var communityId = await SeedCommunityWithMembersAsync(requesterId, offererId);
        var itemId = await SeedItemAsync(communityId, offererId);
        var requestId = await SeedRequestAsync(communityId, requesterId);
        await SeedOfferAsync(communityId, offererId, itemId, requestId);

        using var client = CreateClientWithToken(requesterId);

        var itemsResponse = await client.GetAsync($"/api/items?communityId={communityId}");
        Assert.Equal(HttpStatusCode.OK, itemsResponse.StatusCode);
        var itemsPayload = await itemsResponse.Content.ReadFromJsonAsync<PagedResponseDto<ItemListItemDto>>();
        Assert.NotNull(itemsPayload);
        var item = Assert.Single(itemsPayload!.Items.Where(entry => entry.Id == itemId));
        Assert.NotNull(item.Owner);
        Assert.False(string.IsNullOrWhiteSpace(item.Owner.AvatarUrl));

        var requestsResponse = await client.GetAsync($"/api/requests?communityId={communityId}");
        Assert.Equal(HttpStatusCode.OK, requestsResponse.StatusCode);
        var requestsPayload = await requestsResponse.Content.ReadFromJsonAsync<PagedResponseDto<RequestListItemDto>>();
        Assert.NotNull(requestsPayload);
        var request = Assert.Single(requestsPayload!.Items.Where(entry => entry.Id == requestId));
        Assert.NotNull(request.Owner);
        Assert.False(string.IsNullOrWhiteSpace(request.Owner.AvatarUrl));

        var requestOffersResponse = await client.GetAsync($"/api/requests/{requestId}/offers");
        Assert.Equal(HttpStatusCode.OK, requestOffersResponse.StatusCode);
        var offersPayload = await requestOffersResponse.Content.ReadFromJsonAsync<PagedResponseDto<OfferListItemDto>>();
        Assert.NotNull(offersPayload);
        var offer = Assert.Single(offersPayload!.Items);
        Assert.NotNull(offer.Offerer);
        Assert.False(string.IsNullOrWhiteSpace(offer.Offerer.AvatarUrl));
        Assert.NotNull(offer.Item.Owner);
        Assert.False(string.IsNullOrWhiteSpace(offer.Item.Owner.AvatarUrl));
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

        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<RequestListItemDto>>();
        Assert.NotNull(payload);

        var request = payload!.Items.FirstOrDefault(entry => entry.Id == requestId);
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
        Assert.Equal("createdAt", payload.Sort);
        Assert.Equal("desc", payload.Order);
    }

    [Fact]
    public async Task GetRequests_WithCursorPaginationStatusAndSort_ReturnsNextPage()
    {
        var requesterId = $"requests-cursor-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);

        await SeedRequestAsync(communityId, requesterId, RequestStatus.Open);
        await SeedRequestAsync(communityId, requesterId, RequestStatus.Open);
        await SeedRequestAsync(communityId, requesterId, RequestStatus.Open);
        await SeedRequestAsync(communityId, requesterId, RequestStatus.Closed);

        using var client = CreateClientWithToken(requesterId);

        var firstPageResponse = await client.GetAsync(
            $"/api/requests?communityId={communityId}&status=Open&pageSize=2&sort=createdAt:desc");
        Assert.Equal(HttpStatusCode.OK, firstPageResponse.StatusCode);

        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PagedResponseDto<RequestListItemDto>>();
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage!.Items.Count);
        Assert.Equal(2, firstPage.PageSize);
        Assert.Equal("createdAt", firstPage.Sort);
        Assert.Equal("desc", firstPage.Order);
        Assert.All(firstPage.Items, request => Assert.Equal("Open", request.Status));
        Assert.False(string.IsNullOrWhiteSpace(firstPage.NextCursor));

        var firstPageIds = firstPage.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var secondPageResponse = await client.GetAsync(
            $"/api/requests?communityId={communityId}&status=Open&pageSize=2&sort=createdAt:desc&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);

        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<PagedResponseDto<RequestListItemDto>>();
        Assert.NotNull(secondPage);
        Assert.Single(secondPage!.Items);
        Assert.All(secondPage.Items, request => Assert.Equal("Open", request.Status));
        Assert.DoesNotContain(secondPage.Items[0].Id, firstPageIds);
    }

    [Fact]
    public async Task GetRequestCounts_ReturnsAggregatedBadges()
    {
        var actorId = $"requests-counts-actor-{Guid.NewGuid():N}";
        var otherId = $"requests-counts-other-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(actorId, otherId);
        var soon = DateTime.UtcNow.AddHours(24);
        var later = DateTime.UtcNow.AddDays(10);

        await SeedRequestAsync(communityId, actorId, RequestStatus.Open, soon);
        await SeedRequestAsync(communityId, actorId, RequestStatus.Open, later);
        await SeedRequestAsync(communityId, otherId, RequestStatus.Open, soon);
        await SeedRequestAsync(communityId, actorId, RequestStatus.Closed, soon);

        using var client = CreateClientWithToken(actorId);
        var response = await client.GetAsync($"/api/requests/counts?communityId={communityId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<RequestCountsDto>();
        Assert.NotNull(payload);
        Assert.Equal(3, payload!.Open);
        Assert.Equal(2, payload.MyOpen);
        Assert.Equal(2, payload.ExpiringSoon);
    }

    [Fact]
    public async Task GetRequests_WithIfNoneMatch_ReturnsNotModified()
    {
        var requesterId = $"requests-etag-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        await SeedRequestAsync(communityId, requesterId);

        using var client = CreateClientWithToken(requesterId);
        var firstResponse = await client.GetAsync($"/api/requests?communityId={communityId}&status=Open&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Headers.TryGetValues("ETag", out var etagValues));
        var etag = Assert.Single(etagValues);
        Assert.True((firstResponse.Headers.CacheControl?.ToString() ?? string.Empty)
            .Contains("max-age=", StringComparison.OrdinalIgnoreCase));

        using var secondRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/requests?communityId={communityId}&status=Open&pageSize=20");
        secondRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
    }

    [Fact]
    public async Task GetRequestCounts_WithIfNoneMatch_ReturnsNotModified()
    {
        var requesterId = $"requests-counts-etag-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(requesterId);
        await SeedRequestAsync(communityId, requesterId, RequestStatus.Open, DateTime.UtcNow.AddHours(5));

        using var client = CreateClientWithToken(requesterId);
        var firstResponse = await client.GetAsync($"/api/requests/counts?communityId={communityId}");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Headers.TryGetValues("ETag", out var etagValues));
        var etag = Assert.Single(etagValues);

        using var secondRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/requests/counts?communityId={communityId}");
        secondRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
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
    public async Task GetDashboard_ReturnsAggregatedPreviewsAndCounters()
    {
        var actorUserId = $"dashboard-actor-{Guid.NewGuid():N}";
        var otherMemberId = $"dashboard-other-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(actorUserId, otherMemberId);

        await SeedRequestAsync(communityId, actorUserId);
        await SeedRequestAsync(communityId, actorUserId);
        await SeedRequestAsync(communityId, otherMemberId);

        var availableItemByActor = await SeedItemAsync(communityId, actorUserId, ItemStatus.Available);
        var availableItemByOther = await SeedItemAsync(communityId, otherMemberId, ItemStatus.Available);
        await SeedItemAsync(communityId, actorUserId, ItemStatus.Reserved);

        using var client = CreateClientWithToken(actorUserId);
        var response = await client.GetAsync($"/api/dashboard/{communityId}?previewSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DashboardSummaryDto>();
        Assert.NotNull(payload);

        Assert.Equal(3, payload!.Counters.OpenRequestsTotal);
        Assert.Equal(2, payload.Counters.AvailableItemsTotal);
        Assert.Equal(2, payload.Counters.MyRequestsTotal);

        Assert.True(payload.OpenRequestsPreview.Count <= 2);
        Assert.True(payload.AvailableItemsPreview.Count <= 2);
        Assert.True(payload.MyRequestsPreview.Count <= 2);
        Assert.Contains(payload.AvailableItemsPreview, item => item.Id == availableItemByActor);
        Assert.Contains(payload.AvailableItemsPreview, item => item.Id == availableItemByOther);
        Assert.All(payload.MyRequestsPreview, request => Assert.Equal(actorUserId, request.Owner.Id));
    }

    [Fact]
    public async Task GetDashboard_AsNonMember_ReturnsForbiddenErrorEnvelope()
    {
        var ownerId = $"dashboard-owner-{Guid.NewGuid():N}";
        var outsiderId = $"dashboard-outsider-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        await SeedUserAsync(outsiderId);

        using var client = CreateClientWithToken(outsiderId);
        var response = await client.GetAsync($"/api/dashboard/{communityId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorEnvelopeAsync(response, "forbidden");
    }

    [Fact]
    public async Task GetDashboard_WithIfNoneMatch_ReturnsNotModified()
    {
        var ownerId = $"dashboard-etag-owner-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId);
        await SeedRequestAsync(communityId, ownerId);
        await SeedItemAsync(communityId, ownerId, ItemStatus.Available);

        using var client = CreateClientWithToken(ownerId);
        var firstResponse = await client.GetAsync($"/api/dashboard/{communityId}?previewSize=2");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Headers.TryGetValues("ETag", out var etagValues));
        var etag = Assert.Single(etagValues);

        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/dashboard/{communityId}?previewSize=2");
        secondRequest.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var secondResponse = await client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
    }

    [Fact]
    public async Task GetCommunityRolePermissions_AsOwner_ReturnsRolePermissions()
    {
        var ownerId = $"role-preview-owner-{Guid.NewGuid():N}";
        var memberId = $"role-preview-member-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);

        using var client = CreateClientWithToken(ownerId);
        var response = await client.GetAsync($"/api/communities/{communityId}/roles/Moderator/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CommunityRolePermissionsDto>();
        Assert.NotNull(payload);
        Assert.Equal(communityId, payload!.CommunityId);
        Assert.Equal("Moderator", payload.Role);
        Assert.Contains("requests.moderate", payload.Permissions);
    }

    [Fact]
    public async Task GetCommunityRolePermissions_AsMember_ReturnsForbidden()
    {
        var ownerId = $"role-preview-owner-{Guid.NewGuid():N}";
        var memberId = $"role-preview-member-{Guid.NewGuid():N}";
        var communityId = await SeedCommunityWithMembersAsync(ownerId, memberId);

        using var client = CreateClientWithToken(memberId);
        var response = await client.GetAsync($"/api/communities/{communityId}/roles/Moderator/permissions");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorEnvelopeAsync(response, "forbidden");
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

        var payload = await response.Content.ReadFromJsonAsync<PagedResponseDto<OfferListItemDto>>();
        Assert.NotNull(payload);

        var offer = payload!.Items.FirstOrDefault(entry => entry.Id == offerId);
        Assert.NotNull(offer);
        Assert.NotNull(offer!.Community);
        Assert.Equal(communityId, offer.Community.Id);
        Assert.False(string.IsNullOrWhiteSpace(offer.Community.Name));
        Assert.False(string.IsNullOrWhiteSpace(offer.Community.Slug));
        Assert.NotNull(offer.Offerer);
        Assert.False(string.IsNullOrWhiteSpace(offer.Offerer.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(offer.Offerer.UserName));
        Assert.NotNull(offer.Item);
        Assert.Equal(itemId, offer.Item.Id);
        Assert.False(string.IsNullOrWhiteSpace(offer.Item.Name));
        Assert.Equal("Available", offer.Item.Status);
        Assert.NotNull(offer.Item.Owner);
        Assert.Equal(offererId, offer.Item.Owner.Id);
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
        Assert.NotNull(offer.Item);
        Assert.Equal(itemId, offer.Item.Id);
        Assert.False(string.IsNullOrWhiteSpace(offer.Item.Name));
        Assert.Equal("Available", offer.Item.Status);
        Assert.NotNull(offer.Item.Owner);
        Assert.Equal(offererId, offer.Item.Owner.Id);
        Assert.NotNull(offer.AllowedActions);
        Assert.Contains("view", offer.AllowedActions!);
        Assert.Contains("accept", offer.AllowedActions!);
        Assert.Contains("reject", offer.AllowedActions!);
    }

    private static string GetRequiredEtag(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("ETag", out var values));
        var etag = values!.Single();
        Assert.False(string.IsNullOrWhiteSpace(etag));
        return etag;
    }

    private static async Task AssertPagedEnvelopeAsync(
        HttpResponseMessage response,
        string expectedSort,
        string expectedOrder)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = payload.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("items").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("page").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("pageSize").ValueKind);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("total").ValueKind);
        Assert.Equal(expectedSort, root.GetProperty("sort").GetString());
        Assert.Equal(expectedOrder, root.GetProperty("order").GetString());
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

    private async Task SeedUserAsync(string userId, string? profileImageKey = null)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CondivaDbContext>();

        var existing = await dbContext.Users.FindAsync(userId);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(profileImageKey)
                && !string.Equals(existing.ProfileImageKey, profileImageKey, StringComparison.Ordinal))
            {
                existing.ProfileImageKey = profileImageKey;
                await dbContext.SaveChangesAsync();
            }
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
            LastName = "User",
            ProfileImageKey = profileImageKey
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

    private async Task<string> SeedRequestAsync(
        string communityId,
        string requesterId,
        RequestStatus status = RequestStatus.Open,
        DateTime? neededTo = null)
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
            Status = status,
            CreatedAt = DateTime.UtcNow,
            NeededTo = neededTo
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
