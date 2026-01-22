using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Events.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Events.Endpoints;

public static class EventsEndpoints
{
    public static IEndpointRouteBuilder MapEventsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/events");
        group.RequireAuthorization();
        group.WithTags("Events");

        group.MapGet("/", async (CondivaDbContext dbContext) =>
            await dbContext.Events.ToListAsync());

        group.MapGet("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var evt = await dbContext.Events.FindAsync(id);
            return evt is null ? ApiErrors.NotFound("Event") : Results.Ok(evt);
        });

        group.MapPost("/", async (
            Event body,
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
            if (string.IsNullOrWhiteSpace(body.EntityType))
            {
                return ApiErrors.Required(nameof(body.EntityType));
            }
            if (string.IsNullOrWhiteSpace(body.EntityId))
            {
                return ApiErrors.Required(nameof(body.EntityId));
            }
            if (string.IsNullOrWhiteSpace(body.Action))
            {
                return ApiErrors.Required(nameof(body.Action));
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var actorExists = await dbContext.Users
                .AnyAsync(user => user.Id == actorUserId);
            if (!actorExists)
            {
                return ApiErrors.Invalid("ActorUserId does not exist.");
            }
            if (string.IsNullOrWhiteSpace(body.Id))
            {
                body.Id = Guid.NewGuid().ToString();
            }
            body.CreatedAt = DateTime.UtcNow;
            body.ActorUserId = actorUserId;

            dbContext.Events.Add(body);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/events/{body.Id}", body);
        });

        group.MapPut("/{id}", async (
            string id,
            Event body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var evt = await dbContext.Events.FindAsync(id);
            if (evt is null)
            {
                return ApiErrors.NotFound("Event");
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(body.EntityType))
            {
                return ApiErrors.Required(nameof(body.EntityType));
            }
            if (string.IsNullOrWhiteSpace(body.EntityId))
            {
                return ApiErrors.Required(nameof(body.EntityId));
            }
            if (string.IsNullOrWhiteSpace(body.Action))
            {
                return ApiErrors.Required(nameof(body.Action));
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var actorExists = await dbContext.Users
                .AnyAsync(user => user.Id == actorUserId);
            if (!actorExists)
            {
                return ApiErrors.Invalid("ActorUserId does not exist.");
            }

            evt.CommunityId = body.CommunityId;
            evt.ActorUserId = actorUserId;
            evt.EntityType = body.EntityType;
            evt.EntityId = body.EntityId;
            evt.Action = body.Action;
            evt.Payload = body.Payload;

            await dbContext.SaveChangesAsync();
            return Results.Ok(evt);
        });

        group.MapDelete("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var evt = await dbContext.Events.FindAsync(id);
            if (evt is null)
            {
                return Results.NotFound();
            }

            dbContext.Events.Remove(evt);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        return endpoints;
    }
}
