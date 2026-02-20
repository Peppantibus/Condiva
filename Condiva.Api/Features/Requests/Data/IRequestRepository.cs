using Condiva.Api.Common.Results;
using Condiva.Api.Features.Requests.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Requests.Data;

public interface IRequestRepository
{
    Task<RepositoryResult<CursorPagedResult<Request>>> GetListAsync(
        string communityId,
        string? status,
        string? cursor,
        int? pageSize,
        string? sort,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Request>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<PagedResult<Features.Offers.Models.Offer>>> GetOffersAsync(
        string id,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<PagedResult<Request>>> GetMineAsync(
        string? communityId,
        string? status,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Request>> CreateAsync(Request body, ClaimsPrincipal user, CondivaDbContext dbContext);
    Task<RepositoryResult<Request>> UpdateAsync(string id, Request body, ClaimsPrincipal user, CondivaDbContext dbContext);
    Task<RepositoryResult<bool>> DeleteAsync(string id, ClaimsPrincipal user, CondivaDbContext dbContext);
}
