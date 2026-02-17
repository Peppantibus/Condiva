using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Features.Items.Dtos;

public static class ItemMappings
{
    private const int AvatarPresignTtlSeconds = 300;

    public static void Register(MapperRegistry registry)
    {
        registry.Register<Item, ItemListItemDto>((item, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new ItemListItemDto(
                item.Id,
                item.CommunityId,
                item.OwnerUserId,
                item.Name,
                item.Description,
                item.Category,
                item.ImageKey,
                item.Status.ToString(),
                item.CreatedAt,
                item.UpdatedAt,
                BuildUserSummary(item.OwnerUser, item.OwnerUserId, storageService));
        });

        registry.Register<Item, ItemDetailsDto>((item, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new ItemDetailsDto(
                item.Id,
                item.CommunityId,
                item.OwnerUserId,
                item.Name,
                item.Description,
                item.Category,
                item.ImageKey,
                item.Status.ToString(),
                item.CreatedAt,
                item.UpdatedAt,
                BuildUserSummary(item.OwnerUser, item.OwnerUserId, storageService));
        });
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
}
