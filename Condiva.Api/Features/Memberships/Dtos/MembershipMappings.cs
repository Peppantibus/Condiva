using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Memberships.Models;

namespace Condiva.Api.Features.Memberships.Dtos;

public static class MembershipMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<Membership, MembershipListItemDto>(membership => new MembershipListItemDto(
            membership.Id,
            membership.UserId,
            membership.CommunityId,
            membership.Role.ToString(),
            membership.Status.ToString(),
            membership.InvitedByUserId,
            membership.CreatedAt,
            membership.JoinedAt,
            BuildUserSummary(membership.User, membership.UserId)));

        registry.Register<Membership, MembershipDetailsDto>(membership => new MembershipDetailsDto(
            membership.Id,
            membership.UserId,
            membership.CommunityId,
            membership.Role.ToString(),
            membership.Status.ToString(),
            membership.InvitedByUserId,
            membership.CreatedAt,
            membership.JoinedAt,
            BuildUserSummary(membership.User, membership.UserId)));
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
