using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Condiva.Api.Features.Requests.Dtos;

public static class RequestMappings
{
    private const int AvatarPresignTtlSeconds = 300;

    public static void Register(MapperRegistry registry)
    {
        registry.Register<Request, RequestListItemDto>((request, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new RequestListItemDto(
                request.Id,
                request.CommunityId,
                request.RequesterUserId,
                request.Title,
                request.Description,
                request.ImageKey,
                request.Status.ToString(),
                request.CreatedAt,
                request.NeededFrom,
                request.NeededTo,
                BuildCommunitySummary(request.Community, request.CommunityId),
                BuildUserSummary(request.RequesterUser, request.RequesterUserId, storageService));
        });

        registry.Register<Request, RequestDetailsDto>((request, services) =>
        {
            var storageService = services.GetRequiredService<IR2StorageService>();
            return new RequestDetailsDto(
                request.Id,
                request.CommunityId,
                request.RequesterUserId,
                request.Title,
                request.Description,
                request.ImageKey,
                request.Status.ToString(),
                request.CreatedAt,
                request.NeededFrom,
                request.NeededTo,
                BuildCommunitySummary(request.Community, request.CommunityId),
                BuildUserSummary(request.RequesterUser, request.RequesterUserId, storageService));
        });
    }

    private static CommunitySummaryDto BuildCommunitySummary(Community? community, string fallbackCommunityId)
    {
        if (community is null)
        {
            return new CommunitySummaryDto(fallbackCommunityId, string.Empty, string.Empty);
        }

        return new CommunitySummaryDto(community.Id, community.Name, community.Slug);
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
