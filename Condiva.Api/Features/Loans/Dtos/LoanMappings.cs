using Condiva.Api.Common.Auth.Models;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;

namespace Condiva.Api.Features.Loans.Dtos;

public static class LoanMappings
{
    public static void Register(MapperRegistry registry)
    {
        registry.Register<Loan, LoanListItemDto>(loan => new LoanListItemDto(
            loan.Id,
            loan.CommunityId,
            loan.ItemId,
            loan.LenderUserId,
            loan.BorrowerUserId,
            loan.RequestId,
            loan.OfferId,
            loan.Status.ToString(),
            loan.StartAt,
            loan.DueAt,
            loan.ReturnedAt,
            loan.ReturnRequestedAt,
            loan.ReturnConfirmedAt,
            BuildUserSummary(loan.LenderUser, loan.LenderUserId),
            BuildUserSummary(loan.BorrowerUser, loan.BorrowerUserId),
            BuildItemSummary(loan.Item, loan.ItemId)));

        registry.Register<Loan, LoanDetailsDto>(loan => new LoanDetailsDto(
            loan.Id,
            loan.CommunityId,
            loan.ItemId,
            loan.LenderUserId,
            loan.BorrowerUserId,
            loan.RequestId,
            loan.OfferId,
            loan.Status.ToString(),
            loan.StartAt,
            loan.DueAt,
            loan.ReturnedAt,
            loan.ReturnRequestedAt,
            loan.ReturnConfirmedAt,
            BuildUserSummary(loan.LenderUser, loan.LenderUserId),
            BuildUserSummary(loan.BorrowerUser, loan.BorrowerUserId),
            BuildItemSummary(loan.Item, loan.ItemId)));
    }

    private static UserSummaryDto BuildUserSummary(User? user, string fallbackUserId)
    {
        if (user is null)
        {
            return new UserSummaryDto(fallbackUserId, string.Empty, string.Empty, null);
        }

        var displayName = string.Empty;
        if (!string.IsNullOrWhiteSpace(user.Name) || !string.IsNullOrWhiteSpace(user.LastName))
        {
            displayName = $"{user.Name} {user.LastName}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(user.Username))
        {
            displayName = user.Username;
        }
        else
        {
            displayName = user.Id;
        }

        var userName = user.Username ?? string.Empty;

        return new UserSummaryDto(user.Id, displayName, userName, null);
    }

    private static ItemSummaryDto BuildItemSummary(Item? item, string fallbackItemId)
    {
        if (item is null)
        {
            return new ItemSummaryDto(fallbackItemId, string.Empty, string.Empty, null, string.Empty, string.Empty);
        }

        return new ItemSummaryDto(
            item.Id,
            item.Name,
            item.Description,
            item.Category,
            item.Status.ToString(),
            item.OwnerUserId);
    }
}
