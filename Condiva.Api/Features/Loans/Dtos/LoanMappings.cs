using Condiva.Api.Common.Mapping;
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
            loan.ReturnedAt));

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
            loan.ReturnedAt));
    }
}
