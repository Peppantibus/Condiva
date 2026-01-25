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
            membership.JoinedAt));

        registry.Register<Membership, MembershipDetailsDto>(membership => new MembershipDetailsDto(
            membership.Id,
            membership.UserId,
            membership.CommunityId,
            membership.Role.ToString(),
            membership.Status.ToString(),
            membership.InvitedByUserId,
            membership.CreatedAt,
            membership.JoinedAt));
    }
}
