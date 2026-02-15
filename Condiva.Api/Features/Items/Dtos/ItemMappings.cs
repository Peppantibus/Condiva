using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Items.Models;

namespace Condiva.Api.Features.Items.Dtos;

public static class ItemMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<Item, ItemListItemDto>(item => new ItemListItemDto(
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
            BuildUserSummary(item.OwnerUser, item.OwnerUserId)));

        registry.Register<Item, ItemDetailsDto>(item => new ItemDetailsDto(
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
            BuildUserSummary(item.OwnerUser, item.OwnerUserId)));
    }

    private static UserSummaryDto BuildUserSummary(User? user, string fallbackUserId)
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

        return new UserSummaryDto(user.Id, displayName, userName, null);
    }
}
