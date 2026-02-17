using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Condiva.Api.Features.Memberships.Models;
using Microsoft.EntityFrameworkCore;

namespace Condiva.Api.Common.Auth;

public static class ActorMembershipRoles
{
    public static async Task<MembershipRole?> GetRoleAsync(
        CondivaDbContext dbContext,
        string actorUserId,
        string communityId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Memberships
            .Where(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active
                && membership.CommunityId == communityId)
            .Select(membership => (MembershipRole?)membership.Role)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task<Dictionary<string, MembershipRole>> GetRolesByCommunityAsync(
        CondivaDbContext dbContext,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Memberships
            .Where(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active)
            .ToDictionaryAsync(
                membership => membership.CommunityId,
                membership => membership.Role,
                cancellationToken);
    }
}
