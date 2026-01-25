using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Memberships.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Events.Data;

public sealed class EventRepository : IEventRepository
{
    public async Task<RepositoryResult<IReadOnlyList<Event>>> GetAllAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Event>>.Failure(ApiErrors.Unauthorized());
        }

        var events = await dbContext.Events
            .Join(
                dbContext.Memberships.Where(membership =>
                    membership.UserId == actorUserId
                    && membership.Status == MembershipStatus.Active),
                evt => evt.CommunityId,
                membership => membership.CommunityId,
                (evt, _) => evt)
            .Distinct()
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Event>>.Success(events);
    }

    public async Task<RepositoryResult<Event>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Unauthorized());
        }

        var evt = await dbContext.Events.FindAsync(id);
        return evt is null
            ? RepositoryResult<Event>.Failure(ApiErrors.NotFound("Event"))
            : await EnsureCommunityMemberAsync(evt.CommunityId, actorUserId, dbContext, evt);
    }

    public async Task<RepositoryResult<Event>> CreateAsync(
        Event body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.EntityType))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.EntityType)));
        }
        if (string.IsNullOrWhiteSpace(body.EntityId))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.EntityId)));
        }
        if (string.IsNullOrWhiteSpace(body.Action))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.Action)));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Event>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        if (!CanManageCommunity(membership))
        {
            return RepositoryResult<Event>.Failure(
                ApiErrors.Invalid("User is not allowed to create the event."));
        }
        var actorExists = await dbContext.Users
            .AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        if (string.IsNullOrWhiteSpace(body.Id))
        {
            body.Id = Guid.NewGuid().ToString();
        }
        body.CreatedAt = DateTime.UtcNow;
        body.ActorUserId = actorUserId;

        dbContext.Events.Add(body);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<Event>.Success(body);
    }

    public async Task<RepositoryResult<Event>> UpdateAsync(
        string id,
        Event body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Unauthorized());
        }
        var evt = await dbContext.Events.FindAsync(id);
        if (evt is null)
        {
            return RepositoryResult<Event>.Failure(ApiErrors.NotFound("Event"));
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (!string.Equals(body.CommunityId, evt.CommunityId, StringComparison.Ordinal))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Invalid("CommunityId cannot be changed."));
        }
        if (string.IsNullOrWhiteSpace(body.EntityType))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.EntityType)));
        }
        if (string.IsNullOrWhiteSpace(body.EntityId))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.EntityId)));
        }
        if (string.IsNullOrWhiteSpace(body.Action))
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Required(nameof(body.Action)));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Event>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        if (!CanManageCommunity(membership))
        {
            return RepositoryResult<Event>.Failure(
                ApiErrors.Invalid("User is not allowed to update the event."));
        }
        var actorExists = await dbContext.Users
            .AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Event>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }

        evt.CommunityId = body.CommunityId;
        evt.ActorUserId = actorUserId;
        evt.EntityType = body.EntityType;
        evt.EntityId = body.EntityId;
        evt.Action = body.Action;
        evt.Payload = body.Payload;

        await dbContext.SaveChangesAsync();
        return RepositoryResult<Event>.Success(evt);
    }

    public async Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        var evt = await dbContext.Events.FindAsync(id);
        if (evt is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Event"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == evt.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        if (!CanManageCommunity(membership))
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not allowed to delete the event."));
        }

        dbContext.Events.Remove(evt);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }

    private static async Task<RepositoryResult<Event>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        CondivaDbContext dbContext,
        Event evt)
    {
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Event>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        return RepositoryResult<Event>.Success(evt);
    }
}
