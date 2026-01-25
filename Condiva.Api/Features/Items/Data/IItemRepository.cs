using Condiva.Api.Common.Results;
using Condiva.Api.Features.Items.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Items.Data;

public interface IItemRepository
{
    Task<RepositoryResult<IReadOnlyList<Item>>> GetAllAsync(
        string communityId,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Item>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Item>> CreateAsync(Item body, ClaimsPrincipal user, CondivaDbContext dbContext);
    Task<RepositoryResult<Item>> UpdateAsync(
        string id,
        Item body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
}
