using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Requests.Models;

namespace Condiva.Api.Features.Offers.Models;

public sealed class Offer
{
    public string Id { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public string OffererUserId { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string? Message { get; set; }
    public OfferStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }

    public Community? Community { get; set; }
    public User? OffererUser { get; set; }
    public Request? Request { get; set; }
    public Item? Item { get; set; }
    public Loan? Loan { get; set; }
}

public enum OfferStatus
{
    Open,
    Accepted,
    Rejected,
    Withdrawn
}
