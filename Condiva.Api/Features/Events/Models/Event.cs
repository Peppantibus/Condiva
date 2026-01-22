using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;

namespace Condiva.Api.Features.Events.Models;

public sealed class Event
{
    public string Id { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public DateTime CreatedAt { get; set; }

    public Community? Community { get; set; }
    public User? ActorUser { get; set; }
}
