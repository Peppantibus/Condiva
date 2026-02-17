using Condiva.Api.Common.Results;
using Condiva.Api.Features.Items.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Items.Data;

public interface IItemRepository
{
    Task<RepositoryResult<PagedResult<Item>>> GetAllAsync(
        string communityId,
        string? owner,
        string? status,
        string? category,
        string? search,
        string? sort,
        int? page,
        int? pageSize,
        ClaimsPrincipal user);
    Task<RepositoryResult<Item>> GetByIdAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<Item>> CreateAsync(Item body, ClaimsPrincipal user);
    Task<RepositoryResult<Item>> UpdateAsync(
        string id,
        Item body,
        ClaimsPrincipal user);
    Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user);
}
