using Condiva.Api.Common.Results;
using Condiva.Api.Features.Loans.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Loans.Data;

public interface ILoanRepository
{
    Task<RepositoryResult<PagedResult<Loan>>> GetAllAsync(
        string? communityId,
        string? status,
        DateTime? from,
        DateTime? to,
        int? page,
        int? pageSize,
        ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> GetByIdAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> CreateAsync(Loan body, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> UpdateAsync(string id, Loan body, ClaimsPrincipal user);
    Task<RepositoryResult<bool>> DeleteAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> StartAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> ReturnRequestAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> ReturnConfirmAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> ReturnCancelAsync(string id, ClaimsPrincipal user);
}
