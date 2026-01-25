using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Events.Data;
using Condiva.Api.Features.Events.Dtos;
using Condiva.Api.Features.Events.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

        group.MapGet("/", async (
            ClaimsPrincipal user,
            IEventRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetAllAsync(user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Event, EventListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        });

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IEventRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetByIdAsync(id, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Event, EventDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapPost("/", async (
            CreateEventRequestDto body,
            ClaimsPrincipal user,
            IEventRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var model = new Event
            {
                CommunityId = body.CommunityId,
                EntityType = body.EntityType,
                EntityId = body.EntityId,
                Action = body.Action,
                Payload = body.Payload
            };

            var result = await repository.CreateAsync(model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Event, EventDetailsDto>(result.Data!);
            return Results.Created($"/api/events/{payload.Id}", payload);
        });

        group.MapPut("/{id}", async (
            string id,
            UpdateEventRequestDto body,
            ClaimsPrincipal user,
            IEventRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var model = new Event
            {
                CommunityId = body.CommunityId,
                EntityType = body.EntityType,
                EntityId = body.EntityId,
                Action = body.Action,
                Payload = body.Payload
            };

            var result = await repository.UpdateAsync(id, model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Event, EventDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IEventRepository repository,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.DeleteAsync(id, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        });

        return endpoints;
    }
}
