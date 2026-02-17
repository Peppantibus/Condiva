using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Events.Data;
using Condiva.Api.Common.Dtos;
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
            IMapper mapper) =>
        {
            var result = await repository.GetAllAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Event, EventListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(new PagedResponseDto<EventListItemDto>(
                payload,
                1,
                payload.Count,
                payload.Count,
                "createdAt",
                "desc"));
        })
            .Produces<PagedResponseDto<EventListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IEventRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Event, EventDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<EventDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateEventRequestDto body,
            ClaimsPrincipal user,
            IEventRepository repository,
            IMapper mapper) =>
        {
            var model = new Event
            {
                CommunityId = body.CommunityId,
                EntityType = body.EntityType,
                EntityId = body.EntityId,
                Action = body.Action,
                Payload = body.Payload
            };

            var result = await repository.CreateAsync(model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Event, EventDetailsDto>(result.Data!);
            return Results.Created($"/api/events/{payload.Id}", payload);
        })
            .Produces<EventDetailsDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", async (
            string id,
            UpdateEventRequestDto body,
            ClaimsPrincipal user,
            IEventRepository repository,
            IMapper mapper) =>
        {
            var model = new Event
            {
                CommunityId = body.CommunityId,
                EntityType = body.EntityType,
                EntityId = body.EntityId,
                Action = body.Action,
                Payload = body.Payload
            };

            var result = await repository.UpdateAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Event, EventDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<EventDetailsDto>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IEventRepository repository) =>
        {
            var result = await repository.DeleteAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }
}
