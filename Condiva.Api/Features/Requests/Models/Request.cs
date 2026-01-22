using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Offers.Models;

namespace Condiva.Api.Features.Requests.Models;

public sealed class Request
{
    public string Id { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public string RequesterUserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? NeededFrom { get; set; }
    public DateTime? NeededTo { get; set; }

    public Community? Community { get; set; }
    public User? RequesterUser { get; set; }
    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}

public enum RequestStatus
{
    Open,
    Accepted,
    Closed,
    Canceled
}
