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
    public DateTime? ReturnRequestedAt { get; set; }
    public DateTime? ReturnConfirmedAt { get; set; }

    public Community? Community { get; set; }
    public Item? Item { get; set; }
    public Request? Request { get; set; }
    public Offer? Offer { get; set; }
    public User? LenderUser { get; set; }
    public User? BorrowerUser { get; set; }
}

/// <summary>
/// Loan lifecycle status.
/// Valid transitions:
/// Reserved -> InLoan (POST /api/loans/{id}/start),
/// InLoan -> ReturnRequested (POST /api/loans/{id}/return-request),
/// ReturnRequested -> InLoan (POST /api/loans/{id}/return-cancel),
/// ReturnRequested -> Returned (POST /api/loans/{id}/return-confirm).
/// Expired is reserved for future use and is not set by the API.
/// </summary>
public enum LoanStatus
{
    /// <summary>Loan created and item reserved.</summary>
    Reserved,
    /// <summary>Loan in progress; item is in loan.</summary>
    InLoan,
    /// <summary>Borrower requested return; waiting for lender confirmation.</summary>
    ReturnRequested,
    /// <summary>Loan closed via return; ReturnedAt must be set.</summary>
    Returned,
    /// <summary>Reserved for future use.</summary>
    Expired
}
