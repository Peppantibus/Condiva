using Condiva.Api.Common.Results;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Memberships.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Memberships.Data;

public interface IMembershipRepository
{
    Task<RepositoryResult<IReadOnlyList<Membership>>> GetAllAsync(
        ClaimsPrincipal user);
    Task<RepositoryResult<IReadOnlyList<Community>>> GetMyCommunitiesAsync(
        ClaimsPrincipal user);
    Task<RepositoryResult<Membership>> GetByIdAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<Membership>> CreateAsync(
        Membership body,
        string? enterCode,
        ClaimsPrincipal user);
    Task<RepositoryResult<Membership>> UpdateAsync(
        string id,
        Membership body,
        ClaimsPrincipal user);
    Task<RepositoryResult<Membership>> UpdateRoleAsync(
        string id,
        UpdateMembershipRoleRequest body,
        ClaimsPrincipal user);
    Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user);
    Task<RepositoryResult<bool>> LeaveAsync(
        string communityId,
        ClaimsPrincipal user);
}
