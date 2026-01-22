using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Offers.Models;

namespace Condiva.Api.Features.Items.Models;

public sealed class Item
{
    public string Id { get; set; } = string.Empty;
    public string CommunityId { get; set; } = string.Empty;
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public ItemStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Community? Community { get; set; }
    public User? OwnerUser { get; set; }
    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
}

public enum ItemStatus
{
    Available,
    Reserved,
    InLoan,
    Unavailable
}
