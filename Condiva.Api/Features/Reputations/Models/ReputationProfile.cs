using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;

namespace Condiva.Api.Features.Reputations.Models;

public sealed class ReputationProfile
{
    public string Id { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int Score { get; set; }
    public int LendCount { get; set; }
    public int ReturnCount { get; set; }
    public int OnTimeReturnCount { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Community? Community { get; set; }
    public User? User { get; set; }
}
