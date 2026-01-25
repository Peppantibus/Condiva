using Condiva.Api.Common.Results;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Memberships.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Memberships.Data;

public interface IMembershipRepository
{
    Task<RepositoryResult<IReadOnlyList<Membership>>> GetAllAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<IReadOnlyList<Community>>> GetMyCommunitiesAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Membership>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Membership>> CreateAsync(
        Membership body,
        string? enterCode,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Membership>> UpdateAsync(
        string id,
        Membership body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<Membership>> UpdateRoleAsync(
        string id,
        UpdateMembershipRoleRequest body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
    Task<RepositoryResult<bool>> LeaveAsync(
        string communityId,
        ClaimsPrincipal user,
        CondivaDbContext dbContext);
}
