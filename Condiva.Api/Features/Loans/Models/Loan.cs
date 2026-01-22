using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Models;

namespace Condiva.Api.Features.Loans.Models;

public sealed class Loan
{
    public string Id { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string LenderUserId { get; set; } = string.Empty;
    public string BorrowerUserId { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string? OfferId { get; set; }
    public LoanStatus Status { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime? DueAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    public Community? Community { get; set; }
    public Item? Item { get; set; }
    public Request? Request { get; set; }
    public Offer? Offer { get; set; }
    public User? LenderUser { get; set; }
    public User? BorrowerUser { get; set; }
}

public enum LoanStatus
{
    Reserved,
    InLoan,
    Returned,
    Expired
}
