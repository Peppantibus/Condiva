using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Offers.Dtos;

public sealed record OfferListItemDto(
    string Id,
    string CommunityId,
    string OffererUserId,
    string? RequestId,
    string ItemId,
    OfferItemSummaryDto Item,
    string? Message,
    string Status,
    DateTime CreatedAt,
    CommunitySummaryDto Community,
    UserSummaryDto Offerer,
    string[]? AllowedActions = null);
