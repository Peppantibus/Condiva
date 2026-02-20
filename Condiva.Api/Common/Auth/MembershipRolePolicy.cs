using Condiva.Api.Features.Memberships.Models;

namespace Condiva.Api.Common.Auth;

public static class MembershipRolePolicy
{
    public static bool CanModerateContent(MembershipRole role)
    {
        return role is MembershipRole.Owner or MembershipRole.Admin or MembershipRole.Moderator;
    }

    public static bool CanManageInvites(MembershipRole role)
    {
        return role is MembershipRole.Owner or MembershipRole.Admin;
    }

    public static bool CanManageMembers(MembershipRole role)
    {
        return role is MembershipRole.Owner or MembershipRole.Admin;
    }

    public static bool CanManageCommunitySettings(MembershipRole role)
    {
        return role is MembershipRole.Owner or MembershipRole.Admin;
    }

    public static bool CanChangeRoles(MembershipRole role)
    {
        return role is MembershipRole.Owner or MembershipRole.Admin;
    }

    public static bool IsLeadershipRole(MembershipRole role)
    {
        return role is MembershipRole.Owner or MembershipRole.Admin;
    }
}
