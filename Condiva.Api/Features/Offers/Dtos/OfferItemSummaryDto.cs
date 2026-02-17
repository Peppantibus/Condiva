using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Offers.Dtos;

public sealed record OfferItemSummaryDto(
    string Id,
    string Name,
    string? ImageKey,
    string Status,
    UserSummaryDto Owner);
