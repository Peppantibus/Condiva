using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Items.Endpoints;

public static class ItemsEndpoints
{
    public static IEndpointRouteBuilder MapItemsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/items");
        group.RequireAuthorization();
        group.WithTags("Items");

        group.MapGet("/", async (CondivaDbContext dbContext) =>
            await dbContext.Items.ToListAsync());

        group.MapGet("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var item = await dbContext.Items.FindAsync(id);
            return item is null ? ApiErrors.NotFound("Item") : Results.Ok(item);
        });

        group.MapPost("/", async (
            Item body,
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
            if (string.IsNullOrWhiteSpace(body.OwnerUserId))
            {
                return ApiErrors.Required(nameof(body.OwnerUserId));
            }
            if (!string.Equals(body.OwnerUserId, actorUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("OwnerUserId must match the current user.");
            }
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                return ApiErrors.Required(nameof(body.Name));
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var ownerExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.OwnerUserId);
            if (!ownerExists)
            {
                return ApiErrors.Invalid("OwnerUserId does not exist.");
            }
            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.OwnerUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("OwnerUserId is not a member of the community.");
            }
            if (string.IsNullOrWhiteSpace(body.Id))
            {
                body.Id = Guid.NewGuid().ToString();
            }
            body.CreatedAt = DateTime.UtcNow;
            body.UpdatedAt = null;

            dbContext.Items.Add(body);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/items/{body.Id}", body);
        });

        group.MapPut("/{id}", async (
            string id,
            Item body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var item = await dbContext.Items.FindAsync(id);
            if (item is null)
            {
                return ApiErrors.NotFound("Item");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == item.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to update the item.");
            }
            if (item.Status is ItemStatus.Reserved or ItemStatus.InLoan)
            {
                return ApiErrors.Invalid("Item cannot be updated while reserved or in loan.");
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(body.OwnerUserId))
            {
                return ApiErrors.Required(nameof(body.OwnerUserId));
            }
            if (!CanManageCommunity(membership)
                && !string.Equals(body.OwnerUserId, item.OwnerUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("OwnerUserId cannot be changed.");
            }
            if (string.IsNullOrWhiteSpace(body.Name))
            {
                return ApiErrors.Required(nameof(body.Name));
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var ownerExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.OwnerUserId);
            if (!ownerExists)
            {
                return ApiErrors.Invalid("OwnerUserId does not exist.");
            }
            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.OwnerUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("OwnerUserId is not a member of the community.");
            }

            item.CommunityId = body.CommunityId;
            item.OwnerUserId = body.OwnerUserId;
            item.Name = body.Name;
            item.Description = body.Description;
            item.Category = body.Category;
            item.Status = body.Status;
            item.UpdatedAt = DateTime.UtcNow;

            await dbContext.SaveChangesAsync();
            return Results.Ok(item);
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
            var item = await dbContext.Items.FindAsync(id);
            if (item is null)
            {
                return ApiErrors.NotFound("Item");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == item.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(item.OwnerUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to delete the item.");
            }
            if (item.Status is ItemStatus.Reserved or ItemStatus.InLoan)
            {
                return ApiErrors.Invalid("Item cannot be deleted while reserved or in loan.");
            }

            dbContext.Items.Remove(item);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        return endpoints;
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }
}
