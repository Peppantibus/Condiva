using Condiva.Api.Common.Results;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Offers.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Offers.Data;

public interface IOfferRepository
{
    Task<RepositoryResult<IReadOnlyList<Offer>>> GetAllAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Offer>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<PagedResult<Offer>>> GetMineAsync(
        string? communityId,
        string? status,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Offer>> CreateAsync(Offer body, ClaimsPrincipal user, CondivaDbContext dbContext);
    Task<RepositoryResult<Offer>> UpdateAsync(string id, Offer body, ClaimsPrincipal user, CondivaDbContext dbContext);
    Task<RepositoryResult<bool>> DeleteAsync(string id, ClaimsPrincipal user, CondivaDbContext dbContext);
    Task<RepositoryResult<Loan>> AcceptAsync(
        string id,
        AcceptOfferRequest body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Offer>> RejectAsync(string id, ClaimsPrincipal user, CondivaDbContext dbContext);
    Task<RepositoryResult<Offer>> WithdrawAsync(string id, ClaimsPrincipal user, CondivaDbContext dbContext);
}
