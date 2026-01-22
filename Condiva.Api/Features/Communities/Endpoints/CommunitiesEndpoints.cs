using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Communities.Endpoints;

public static class CommunitiesEndpoints
{
    public static IEndpointRouteBuilder MapCommunitiesEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/communities");
        group.RequireAuthorization();
        group.WithTags("Communities");

        group.MapGet("/", async (CondivaDbContext dbContext) =>
            await dbContext.Communities.ToListAsync());

        group.MapGet("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var community = await dbContext.Communities.FindAsync(id);
            return community is null ? ApiErrors.NotFound("Community") : Results.Ok(community);
        });

        group.MapGet("/{id}/invite-code", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var community = await dbContext.Communities.FindAsync(id);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == id
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);

            if (membership is null || !CanManageInvites(membership))
            {
                return ApiErrors.Invalid("User is not allowed to manage invites.");
            }

            return Results.Ok(new { enterCode = community.EnterCode });
        });

        group.MapPost("/{id}/invite-code/rotate", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var community = await dbContext.Communities.FindAsync(id);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == id
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);

            if (membership is null || membership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Invalid("User is not allowed to rotate invites.");
            }

            community.EnterCode = CreateEnterCode();
            community.EnterCodeExpiresAt = CreateEnterCodeExpiry();
            await dbContext.SaveChangesAsync();

            return Results.Ok(new { enterCode = community.EnterCode });
        });

        group.MapPost("/join", async (
            JoinCommunityRequest body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(body.EnterCode))
            {
                return ApiErrors.Required(nameof(body.EnterCode));
            }

            var community = await dbContext.Communities
                .FirstOrDefaultAsync(c => c.EnterCode == body.EnterCode);
            if (community is null)
            {
                return ApiErrors.Invalid("EnterCode is invalid.");
            }
            if (community.EnterCodeExpiresAt <= DateTime.UtcNow)
            {
                return ApiErrors.Invalid("EnterCode has expired.");
            }

            var existingMembership = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == community.Id
                && membership.UserId == actorUserId);
            if (existingMembership)
            {
                return ApiErrors.Invalid("User is already a member of the community.");
            }

            var member = new Membership
            {
                Id = Guid.NewGuid().ToString(),
                UserId = actorUserId,
                CommunityId = community.Id,
                Role = MembershipRole.Member,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow,
                JoinedAt = DateTime.UtcNow
            };

            dbContext.Memberships.Add(member);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/memberships/{member.Id}", member);
        });

        group.MapGet("/{id}/requests/feed", async (
            string id,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == id);
            if (!communityExists)
            {
                return ApiErrors.NotFound("Community");
            }

            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == id && membership.UserId == actorUserId);
            if (!isMember)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            var requestStatus = RequestStatus.Open;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<RequestStatus>(status, true, out requestStatus))
                {
                    return ApiErrors.Invalid("Invalid status filter.");
                }
            }

            var pageNumber = page.GetValueOrDefault(1);
            var size = pageSize.GetValueOrDefault(20);
            if (pageNumber <= 0 || size <= 0 || size > 100)
            {
                return ApiErrors.Invalid("Invalid pagination parameters.");
            }

            var query = dbContext.Requests
                .Where(request => request.CommunityId == id && request.Status == requestStatus);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(request => request.CreatedAt)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();

            return Results.Ok(new
            {
                items,
                page = pageNumber,
                pageSize = size,
                total
            });
        });

        group.MapGet("/{id}/items/available", async (
            string id,
            string? category,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == id);
            if (!communityExists)
            {
                return ApiErrors.NotFound("Community");
            }

            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == id && membership.UserId == actorUserId);
            if (!isMember)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            var pageNumber = page.GetValueOrDefault(1);
            var size = pageSize.GetValueOrDefault(20);
            if (pageNumber <= 0 || size <= 0 || size > 100)
            {
                return ApiErrors.Invalid("Invalid pagination parameters.");
            }

            var query = dbContext.Items
                .Where(item => item.CommunityId == id && item.Status == ItemStatus.Available);

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(item => item.Category == category);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(item => item.CreatedAt)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();

            return Results.Ok(new
            {
                items,
                page = pageNumber,
                pageSize = size,
                total
            });
        });

        group.MapPost("/", async (Community body, CondivaDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                return ApiErrors.Required(nameof(body.Name));
            }
            if (string.IsNullOrWhiteSpace(body.Slug))
            {
                return ApiErrors.Required(nameof(body.Slug));
            }
            if (string.IsNullOrWhiteSpace(body.CreatedByUserId))
            {
                return ApiErrors.Required(nameof(body.CreatedByUserId));
            }
            var creatorExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.CreatedByUserId);
            if (!creatorExists)
            {
                return ApiErrors.Invalid("CreatedByUserId does not exist.");
            }
            if (string.IsNullOrWhiteSpace(body.Id))
            {
                body.Id = Guid.NewGuid().ToString();
            }
            body.EnterCode = CreateEnterCode();
            body.EnterCodeExpiresAt = CreateEnterCodeExpiry();
            body.CreatedAt = DateTime.UtcNow;

            dbContext.Communities.Add(body);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/communities/{body.Id}", body);
        });

        group.MapPut("/{id}", async (
            string id,
            Community body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var community = await dbContext.Communities.FindAsync(id);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == id
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null || membership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Invalid("User is not allowed to update the community.");
            }
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                return ApiErrors.Required(nameof(body.Name));
            }
            if (string.IsNullOrWhiteSpace(body.Slug))
            {
                return ApiErrors.Required(nameof(body.Slug));
            }
            if (string.IsNullOrWhiteSpace(body.CreatedByUserId))
            {
                return ApiErrors.Required(nameof(body.CreatedByUserId));
            }
            var creatorExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.CreatedByUserId);
            if (!creatorExists)
            {
                return ApiErrors.Invalid("CreatedByUserId does not exist.");
            }

            community.Name = body.Name;
            community.Slug = body.Slug;
            community.Description = body.Description;
            community.CreatedByUserId = body.CreatedByUserId;

            await dbContext.SaveChangesAsync();
            return Results.Ok(community);
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
            var community = await dbContext.Communities.FindAsync(id);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == id
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null || membership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Invalid("User is not allowed to delete the community.");
            }

            dbContext.Communities.Remove(community);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        return endpoints;
    }

    private static bool CanManageInvites(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }

    private static string CreateEnterCode()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static DateTime CreateEnterCodeExpiry()
    {
        return DateTime.UtcNow.AddDays(7);
    }

    public sealed record JoinCommunityRequest(string? EnterCode);
}
