using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Requests.Models;

namespace Condiva.Api.Features.Requests.Dtos;

public static class RequestMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<Request, RequestListItemDto>(request => new RequestListItemDto(
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
            BuildUserSummary(request.RequesterUser, request.RequesterUserId)));

        registry.Register<Request, RequestDetailsDto>(request => new RequestDetailsDto(
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
            BuildUserSummary(request.RequesterUser, request.RequesterUserId)));
    }

    private static CommunitySummaryDto BuildCommunitySummary(Community? community, string fallbackCommunityId)
    {
        if (community is null)
        {
            return new CommunitySummaryDto(fallbackCommunityId, string.Empty, string.Empty);
        }

        return new CommunitySummaryDto(community.Id, community.Name, community.Slug);
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
