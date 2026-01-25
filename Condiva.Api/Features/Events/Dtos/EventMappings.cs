using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Events.Models;

namespace Condiva.Api.Features.Events.Dtos;

public static class EventMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<Event, EventListItemDto>(evt => new EventListItemDto(
            evt.Id,
            evt.CommunityId,
            evt.ActorUserId,
            evt.EntityType,
            evt.EntityId,
            evt.Action,
            evt.Payload,
            evt.CreatedAt));

        registry.Register<Event, EventDetailsDto>(evt => new EventDetailsDto(
            evt.Id,
            evt.CommunityId,
            evt.ActorUserId,
            evt.EntityType,
            evt.EntityId,
            evt.Action,
            evt.Payload,
            evt.CreatedAt));
    }
}
