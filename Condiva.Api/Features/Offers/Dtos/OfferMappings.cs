using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Features.Offers.Dtos;

public static class OfferMappings
{
    private const int AvatarPresignTtlSeconds = 300;

    public static void Register(MapperRegistry registry)
    {
        registry.Register<Offer, OfferListItemDto>((offer, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new OfferListItemDto(
                offer.Id,
                offer.CommunityId,
                offer.OffererUserId,
                offer.RequestId,
                offer.ItemId,
                BuildItemSummary(offer.Item, offer.ItemId, storageService),
                offer.Message,
                offer.Status.ToString(),
                offer.CreatedAt,
                BuildCommunitySummary(offer.Community, offer.CommunityId),
                BuildUserSummary(offer.OffererUser, offer.OffererUserId, storageService));
        });

        registry.Register<Offer, OfferDetailsDto>((offer, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new OfferDetailsDto(
                offer.Id,
                offer.CommunityId,
                offer.OffererUserId,
                offer.RequestId,
                offer.ItemId,
                BuildItemSummary(offer.Item, offer.ItemId, storageService),
                offer.Message,
                offer.Status.ToString(),
                offer.CreatedAt,
                BuildCommunitySummary(offer.Community, offer.CommunityId),
                BuildUserSummary(offer.OffererUser, offer.OffererUserId, storageService));
        });

        registry.Register<Offer, OfferStatusResponseDto>(offer => new OfferStatusResponseDto(
            offer.Id,
            offer.Status.ToString()));
    }

    private static UserSummaryDto BuildUserSummary(
        User? user,
        string fallbackUserId,
        IR2StorageService storageService)
    {
        if (user is null)
        {
            return new UserSummaryDto(fallbackUserId, string.Empty, string.Empty, null);
        }

        var displayName = string.Empty;
        if (!string.IsNullOrWhiteSpace(user.Name) || !string.IsNullOrWhiteSpace(user.LastName))
        {
            displayName = $"{user.Name} {user.LastName}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(user.Username))
        {
            displayName = user.Username;
        }
        else
        {
            displayName = user.Id;
        }

        var userName = user.Username ?? string.Empty;
        var avatarUrl = string.IsNullOrWhiteSpace(user.ProfileImageKey)
            ? null
            : storageService.GeneratePresignedGetUrl(user.ProfileImageKey, AvatarPresignTtlSeconds);

        return new UserSummaryDto(user.Id, displayName, userName, avatarUrl);
    }

    private static CommunitySummaryDto BuildCommunitySummary(Community? community, string fallbackCommunityId)
    {
        if (community is null)
        {
            return new CommunitySummaryDto(fallbackCommunityId, string.Empty, string.Empty);
        }

        return new CommunitySummaryDto(community.Id, community.Name, community.Slug);
    }

    private static OfferItemSummaryDto BuildItemSummary(
        Item? item,
        string fallbackItemId,
        IR2StorageService storageService)
    {
        if (item is null)
        {
            return new OfferItemSummaryDto(
                fallbackItemId,
                string.Empty,
                null,
                string.Empty,
                new UserSummaryDto(string.Empty, string.Empty, string.Empty, null));
        }

        return new OfferItemSummaryDto(
            item.Id,
            item.Name,
            item.ImageKey,
            item.Status.ToString(),
            BuildUserSummary(item.OwnerUser, item.OwnerUserId, storageService));
    }
}
