using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Features.Memberships.Dtos;

public static class MembershipMappings
{
    private const int AvatarPresignTtlSeconds = 300;

    public static void Register(MapperRegistry registry)
    {
        registry.Register<Membership, MembershipListItemDto>((membership, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new MembershipListItemDto(
                membership.Id,
                membership.UserId,
                membership.CommunityId,
                membership.Role.ToString(),
                membership.Status.ToString(),
                membership.InvitedByUserId,
                membership.CreatedAt,
                membership.JoinedAt,
                BuildUserSummary(membership.User, membership.UserId, storageService));
        });

        registry.Register<Membership, MembershipDetailsDto>((membership, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new MembershipDetailsDto(
                membership.Id,
                membership.UserId,
                membership.CommunityId,
                membership.Role.ToString(),
                membership.Status.ToString(),
                membership.InvitedByUserId,
                membership.CreatedAt,
                membership.JoinedAt,
                BuildUserSummary(membership.User, membership.UserId, storageService));
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
