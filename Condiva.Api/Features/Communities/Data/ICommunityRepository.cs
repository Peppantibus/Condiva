using Condiva.Api.Common.Results;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Requests.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Communities.Data;

public interface ICommunityRepository
{
    Task<RepositoryResult<IReadOnlyList<Community>>> GetAllAsync(
        ClaimsPrincipal user);
    Task<RepositoryResult<Community>> GetByIdAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<InviteCodeInfo>> GetInviteCodeAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<InviteCodeInfo>> RotateInviteCodeAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<Membership>> JoinAsync(
        string? enterCode,
        ClaimsPrincipal user);
    Task<RepositoryResult<PagedResult<Request>>> GetRequestsFeedAsync(
        string id,
        string? status,
        bool? excludingMine,
        int? page,
        int? pageSize,
        ClaimsPrincipal user);
    Task<RepositoryResult<PagedResult<Item>>> GetAvailableItemsAsync(
        string id,
        string? category,
        int? page,
        int? pageSize,
        ClaimsPrincipal user);
    Task<RepositoryResult<Community>> CreateAsync(
        Community body,
        ClaimsPrincipal user);
    Task<RepositoryResult<Community>> UpdateAsync(
        string id,
        Community body,
        ClaimsPrincipal user);
    Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user);
}
