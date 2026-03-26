using Condiva.Api.Common.Auth.Models;
using System.Text.Json.Serialization;

namespace Condiva.Api.Features.Communities.Models;

public sealed class CommunityBannedTerm
{
    public string Id { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public string Term { get; set; } = string.Empty;
    [JsonIgnore]
    public string NormalizedTerm { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public Community? Community { get; set; }
    public User? CreatedByUser { get; set; }
}
