using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Communities.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Memberships.Endpoints;

public static class MembershipsEndpoints
{
    public static IEndpointRouteBuilder MapMembershipsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/memberships");
        group.RequireAuthorization();
        group.WithTags("Memberships");

        group.MapGet("/", async (CondivaDbContext dbContext) =>
            await dbContext.Memberships.ToListAsync());

        group.MapGet("/me/communities", async (
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var communities = await dbContext.Memberships
                .Where(membership => membership.UserId == actorUserId)
                .Join(
                    dbContext.Communities,
                    membership => membership.CommunityId,
                    community => community.Id,
                    (_, community) => community)
                .Distinct()
                .ToListAsync();

            return Results.Ok(communities);
        });

        group.MapGet("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var membership = await dbContext.Memberships.FindAsync(id);
            return membership is null ? ApiErrors.NotFound("Membership") : Results.Ok(membership);
        });

        group.MapPost("/", async (
            Membership body,
            string? enterCode,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(enterCode))
            {
                return ApiErrors.Required(nameof(enterCode));
            }
            var userExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
            if (!userExists)
            {
                return ApiErrors.Invalid("UserId does not exist.");
            }
            var community = await dbContext.Communities.FindAsync(body.CommunityId);
            if (community is null)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            if (!string.Equals(community.EnterCode, enterCode, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("EnterCode is invalid.");
            }
            if (community.EnterCodeExpiresAt <= DateTime.UtcNow)
            {
                return ApiErrors.Invalid("EnterCode has expired.");
            }
            var existingMembership = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId && membership.UserId == actorUserId);
            if (existingMembership)
            {
                return ApiErrors.Invalid("User is already a member of the community.");
            }
            if (string.IsNullOrWhiteSpace(body.Id))
            {
                body.Id = Guid.NewGuid().ToString();
            }
            body.UserId = actorUserId;
            body.Role = MembershipRole.Member;
            body.Status = MembershipStatus.Active;
            body.InvitedByUserId = null;
            body.CreatedAt = DateTime.UtcNow;
            body.JoinedAt = DateTime.UtcNow;

            dbContext.Memberships.Add(body);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/memberships/{body.Id}", body);
        });

        group.MapPut("/{id}", async (
            string id,
            Membership body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var membership = await dbContext.Memberships.FindAsync(id);
            if (membership is null)
            {
                return ApiErrors.NotFound("Membership");
            }
            var actorMembership = await dbContext.Memberships.FirstOrDefaultAsync(actor =>
                actor.CommunityId == membership.CommunityId
                && actor.UserId == actorUserId
                && actor.Status == MembershipStatus.Active);
            if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Invalid("User is not allowed to update membership.");
            }
            if (string.IsNullOrWhiteSpace(body.UserId))
            {
                return ApiErrors.Required(nameof(body.UserId));
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            var userExists = await dbContext.Users.AnyAsync(user => user.Id == body.UserId);
            if (!userExists)
            {
                return ApiErrors.Invalid("UserId does not exist.");
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            if (!string.IsNullOrWhiteSpace(body.InvitedByUserId))
            {
                var inviterExists = await dbContext.Users
                    .AnyAsync(user => user.Id == body.InvitedByUserId);
                if (!inviterExists)
                {
                    return ApiErrors.Invalid("InvitedByUserId does not exist.");
                }
            }

            membership.UserId = body.UserId;
            membership.CommunityId = body.CommunityId;
            membership.Role = body.Role;
            membership.Status = body.Status;
            membership.InvitedByUserId = body.InvitedByUserId;
            membership.JoinedAt = body.JoinedAt;

            await dbContext.SaveChangesAsync();
            return Results.Ok(membership);
        });

        group.MapPost("/{id}/role", async (
            string id,
            UpdateMembershipRoleRequest body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(body.Role))
            {
                return ApiErrors.Required(nameof(body.Role));
            }
            if (!Enum.TryParse<MembershipRole>(body.Role, true, out var newRole))
            {
                return ApiErrors.Invalid("Invalid role.");
            }

            var membership = await dbContext.Memberships.FindAsync(id);
            if (membership is null)
            {
                return ApiErrors.NotFound("Membership");
            }

            var actorMembership = await dbContext.Memberships.FirstOrDefaultAsync(actor =>
                actor.CommunityId == membership.CommunityId
                && actor.UserId == actorUserId
                && actor.Status == MembershipStatus.Active);
            if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Invalid("User is not allowed to change roles.");
            }
            if (membership.UserId == actorUserId)
            {
                return ApiErrors.Invalid("Owner role cannot be changed by the same user.");
            }
            if (membership.Role == MembershipRole.Owner && newRole != MembershipRole.Owner)
            {
                var otherOwners = await dbContext.Memberships.AnyAsync(other =>
                    other.CommunityId == membership.CommunityId
                    && other.Role == MembershipRole.Owner
                    && other.UserId != membership.UserId
                    && other.Status == MembershipStatus.Active);
                if (!otherOwners)
                {
                    return ApiErrors.Invalid("At least one active owner is required.");
                }
            }

            membership.Role = newRole;
            await dbContext.SaveChangesAsync();
            return Results.Ok(membership);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var membership = await dbContext.Memberships.FindAsync(id);
            if (membership is null)
            {
                return ApiErrors.NotFound("Membership");
            }
            var actorMembership = await dbContext.Memberships.FirstOrDefaultAsync(actor =>
                actor.CommunityId == membership.CommunityId
                && actor.UserId == actorUserId
                && actor.Status == MembershipStatus.Active);
            if (actorMembership is null || actorMembership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Invalid("User is not allowed to remove members.");
            }
            if (membership.Role == MembershipRole.Owner)
            {
                var canRemoveOwner = await HasAnotherActiveOwner(membership, dbContext);
                if (!canRemoveOwner)
                {
                    return ApiErrors.Invalid("At least one active owner is required.");
                }
            }

            dbContext.Memberships.Remove(membership);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/leave/{communityId}", async (
            string communityId,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(communityId))
            {
                return ApiErrors.Required(nameof(communityId));
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(member =>
                member.CommunityId == communityId
                && member.UserId == actorUserId
                && member.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.NotFound("Membership");
            }
            if (membership.Role == MembershipRole.Owner)
            {
                var canLeaveOwner = await HasAnotherActiveOwner(membership, dbContext);
                if (!canLeaveOwner)
                {
                    return ApiErrors.Invalid("At least one active owner is required.");
                }
            }

            dbContext.Memberships.Remove(membership);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        return endpoints;
    }

    private static async Task<bool> HasAnotherActiveOwner(
        Membership membership,
        CondivaDbContext dbContext)
    {
        return await dbContext.Memberships.AnyAsync(other =>
            other.CommunityId == membership.CommunityId
            && other.Role == MembershipRole.Owner
            && other.UserId != membership.UserId
            && other.Status == MembershipStatus.Active);
    }

    public sealed record UpdateMembershipRoleRequest(string? Role);
}
