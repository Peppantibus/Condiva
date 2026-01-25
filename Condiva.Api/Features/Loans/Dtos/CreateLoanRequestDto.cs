namespace Condiva.Api.Features.Loans.Dtos;

public sealed record CreateLoanRequestDto(
    string CommunityId,
    string ItemId,
    string LenderUserId,
    string BorrowerUserId,
    string? RequestId,
    string? OfferId,
    string Status,
    DateTime? StartAt,
    DateTime? DueAt);
