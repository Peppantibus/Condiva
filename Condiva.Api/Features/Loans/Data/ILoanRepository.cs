using Condiva.Api.Common.Results;
using Condiva.Api.Features.Loans.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Loans.Data;

public interface ILoanRepository
{
    Task<RepositoryResult<IReadOnlyList<Loan>>> GetAllAsync(
        ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> GetByIdAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> CreateAsync(Loan body, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> UpdateAsync(string id, Loan body, ClaimsPrincipal user);
    Task<RepositoryResult<bool>> DeleteAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> StartAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<Loan>> ReturnAsync(string id, ClaimsPrincipal user);
}
