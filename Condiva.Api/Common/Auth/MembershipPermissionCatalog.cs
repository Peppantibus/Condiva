using Condiva.Api.Features.Memberships.Models;

namespace Condiva.Api.Common.Auth;

public static class MembershipPermissionCatalog
{
    public static class Permissions
    {
        public const string CommunityRead = "community.read";
        public const string CommunityUpdate = "community.update";
        public const string CommunityDelete = "community.delete";
        public const string CommunityImageManage = "community.image.manage";
        public const string CommunityInvitesRead = "community.invites.read";
        public const string CommunityInvitesManage = "community.invites.manage";
        public const string MembersRead = "members.read";
        public const string MembersManage = "members.manage";
        public const string MembersRoleUpdate = "members.role.update";
        public const string ItemsCreate = "items.create";
        public const string ItemsManageOwn = "items.manage.own";
        public const string ItemsModerate = "items.moderate";
        public const string RequestsCreate = "requests.create";
        public const string RequestsManageOwn = "requests.manage.own";
        public const string RequestsModerate = "requests.moderate";
        public const string OffersCreate = "offers.create";
        public const string OffersManageOwn = "offers.manage.own";
        public const string OffersModerate = "offers.moderate";
        public const string LoansManageParticipant = "loans.manage.participant";
        public const string LoansModerate = "loans.moderate";
        public const string EventsModerate = "events.moderate";
    }

    private static readonly string[] MemberPermissions =
    [
        Permissions.CommunityRead,
        Permissions.MembersRead,
        Permissions.ItemsCreate,
        Permissions.ItemsManageOwn,
        Permissions.RequestsCreate,
        Permissions.RequestsManageOwn,
        Permissions.OffersCreate,
        Permissions.OffersManageOwn,
        Permissions.LoansManageParticipant
    ];

    private static readonly string[] ModeratorPermissions =
    [
        ..MemberPermissions,
        Permissions.ItemsModerate,
        Permissions.RequestsModerate,
        Permissions.OffersModerate,
        Permissions.LoansModerate,
        Permissions.EventsModerate
    ];

    private static readonly string[] LeadershipPermissions =
    [
        Permissions.CommunityRead,
        Permissions.CommunityUpdate,
        Permissions.CommunityDelete,
        Permissions.CommunityImageManage,
        Permissions.CommunityInvitesRead,
        Permissions.CommunityInvitesManage,
        Permissions.MembersRead,
        Permissions.MembersManage,
        Permissions.MembersRoleUpdate,
        Permissions.ItemsCreate,
        Permissions.ItemsManageOwn,
        Permissions.ItemsModerate,
        Permissions.RequestsCreate,
        Permissions.RequestsManageOwn,
        Permissions.RequestsModerate,
        Permissions.OffersCreate,
        Permissions.OffersManageOwn,
        Permissions.OffersModerate,
        Permissions.LoansManageParticipant,
        Permissions.LoansModerate,
        Permissions.EventsModerate
    ];

    private static readonly string[] InactiveMembershipPermissions =
    [
        Permissions.CommunityRead
    ];

    public static string[] ForMembership(Membership membership)
    {
        if (membership.Status != MembershipStatus.Active)
        {
            return InactiveMembershipPermissions.ToArray();
        }

        return ForRole(membership.Role);
    }

    public static string[] ForRole(MembershipRole role)
    {
        return role switch
        {
            MembershipRole.Owner => LeadershipPermissions.ToArray(),
            MembershipRole.Admin => LeadershipPermissions.ToArray(),
            MembershipRole.Moderator => ModeratorPermissions.ToArray(),
            _ => MemberPermissions.ToArray()
        };
    }
}
