using Condiva.Api.Common.Results;
using Condiva.Api.Features.Notifications.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Notifications.Data;

public interface INotificationRepository
{
    Task<RepositoryResult<PagedResult<Notification>>> GetPagedAsync(
        string? communityId,
        bool? unreadOnly,
        int? page,
        int? pageSize,
        ClaimsPrincipal user);
    Task<RepositoryResult<Notification>> GetByIdAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<Notification>> MarkReadAsync(string id, ClaimsPrincipal user);
    Task<RepositoryResult<IReadOnlyList<Notification>>> MarkReadAsync(
        IReadOnlyList<string> ids,
        ClaimsPrincipal user);
    Task<RepositoryResult<int>> GetUnreadCountAsync(
        string? communityId,
        ClaimsPrincipal user);
}
