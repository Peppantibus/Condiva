namespace Condiva.Api.Features.Loans.Dtos;

public sealed record UpdateLoanRequestDto(
    string CommunityId,
    string ItemId,
    string LenderUserId,
    string BorrowerUserId,
    string? RequestId,
    string? OfferId,
    string Status,
    DateTime? StartAt,
    DateTime? DueAt,
    DateTime? ReturnedAt);
