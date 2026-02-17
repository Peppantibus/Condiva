using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Offers.Dtos;

public sealed record OfferDetailsDto(
    string Id,
    string CommunityId,
    string OffererUserId,
    string? RequestId,
    string ItemId,
    string? Message,
    string Status,
    DateTime CreatedAt,
    CommunitySummaryDto Community,
    UserSummaryDto Offerer,
    string[]? AllowedActions = null);
