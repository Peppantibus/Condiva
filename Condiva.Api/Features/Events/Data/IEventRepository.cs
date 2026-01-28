using Condiva.Api.Common.Results;
using Condiva.Api.Features.Events.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Events.Data;

public interface IEventRepository
{
    Task<RepositoryResult<IReadOnlyList<Event>>> GetAllAsync(
        ClaimsPrincipal user);
    Task<RepositoryResult<Event>> GetByIdAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<Event>> CreateAsync(
        Event body,
        ClaimsPrincipal user);
    Task<RepositoryResult<Event>> UpdateAsync(
        string id,
        Event body,
        ClaimsPrincipal user);
    Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user);
}
