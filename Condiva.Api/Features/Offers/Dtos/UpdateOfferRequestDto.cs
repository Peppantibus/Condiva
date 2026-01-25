namespace Condiva.Api.Features.Offers.Dtos;

public sealed record UpdateOfferRequestDto(
    string CommunityId,
    string OffererUserId,
    string? RequestId,
    string ItemId,
    string? Message,
    string Status);
