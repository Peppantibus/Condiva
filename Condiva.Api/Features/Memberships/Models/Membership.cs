using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;

namespace Condiva.Api.Features.Memberships.Models;

public sealed class Membership
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public MembershipRole Role { get; set; }
    public MembershipStatus Status { get; set; }
    public string? InvitedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? JoinedAt { get; set; }

    public User? User { get; set; }
    public Community? Community { get; set; }
}

public enum MembershipRole
{
    Owner,
    Admin,
    Member,
    Moderator
}

public enum MembershipStatus
{
    Invited,
    Active,
    Suspended
}
