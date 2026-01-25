using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Reputations.Models;
using Condiva.Api.Features.Requests.Models;
using System.Text.Json.Serialization;

namespace Condiva.Api.Features.Communities.Models;

public sealed class Community
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    [JsonIgnore]
    public string EnterCode { get; set; } = string.Empty;
    [JsonIgnore]
    public DateTime EnterCodeExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<Item> Items { get; set; } = new List<Item>();
    public ICollection<Request> Requests { get; set; } = new List<Request>();
    public ICollection<Offer> Offers { get; set; } = new List<Offer>();
    public ICollection<Loan> Loans { get; set; } = new List<Loan>();
    public ICollection<Event> Events { get; set; } = new List<Event>();
    public ICollection<ReputationProfile> Reputations { get; set; } = new List<ReputationProfile>();
}
