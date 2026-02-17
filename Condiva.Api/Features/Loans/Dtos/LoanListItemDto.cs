using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Loans.Dtos;

public sealed record LoanListItemDto(
    string Id,
    string CommunityId,
    string ItemId,
    string LenderUserId,
    string BorrowerUserId,
    string? RequestId,
    string? OfferId,
    string Status,
    DateTime StartAt,
    DateTime? DueAt,
    DateTime? ReturnedAt,
    DateTime? ReturnRequestedAt,
    DateTime? ReturnConfirmedAt,
    UserSummaryDto Lender,
    UserSummaryDto Borrower,
    ItemSummaryDto Item,
    string[]? AllowedActions = null);
