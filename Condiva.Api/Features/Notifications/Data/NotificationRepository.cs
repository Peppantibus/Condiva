using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Notifications.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Notifications.Data;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly CondivaDbContext _dbContext;

    public NotificationRepository(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RepositoryResult<PagedResult<Notification>>> GetPagedAsync(
        string? communityId,
        bool? unreadOnly,
        int? page,
        int? pageSize,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Notification>>.Failure(ApiErrors.Unauthorized());
        }

        var pageNumber = page.GetValueOrDefault(1);
        var size = pageSize.GetValueOrDefault(20);
        if (pageNumber <= 0 || size <= 0 || size > 100)
        {
            return RepositoryResult<PagedResult<Notification>>.Failure(
                ApiErrors.Invalid("Invalid pagination parameters."));
        }

        var query = _dbContext.Notifications
            .Where(notification => notification.RecipientUserId == actorUserId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(communityId))
        {
            query = query.Where(notification => notification.CommunityId == communityId);
        }

        if (unreadOnly == true)
        {
            query = query.Where(notification => notification.ReadAt == null);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync();

        return RepositoryResult<PagedResult<Notification>>.Success(
            new PagedResult<Notification>(items, pageNumber, size, total));
    }

    public async Task<RepositoryResult<Notification>> GetByIdAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Notification>.Failure(ApiErrors.Unauthorized());
        }

        var notification = await _dbContext.Notifications.FindAsync(id);
        if (notification is null)
        {
            return RepositoryResult<Notification>.Failure(ApiErrors.NotFound("Notification"));
        }
        if (!string.Equals(notification.RecipientUserId, actorUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Notification>.Failure(ApiErrors.Forbidden("Access denied."));
        }

        return RepositoryResult<Notification>.Success(notification);
    }

    public async Task<RepositoryResult<Notification>> MarkReadAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Notification>.Failure(ApiErrors.Unauthorized());
        }

        var notification = await _dbContext.Notifications.FindAsync(id);
        if (notification is null)
        {
            return RepositoryResult<Notification>.Failure(ApiErrors.NotFound("Notification"));
        }
        if (!string.Equals(notification.RecipientUserId, actorUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Notification>.Failure(ApiErrors.Forbidden("Access denied."));
        }

        notification.ReadAt ??= DateTime.UtcNow;
        if (notification.Status == NotificationStatus.Pending)
        {
            notification.Status = NotificationStatus.Delivered;
        }

        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Notification>.Success(notification);
    }

    public async Task<RepositoryResult<IReadOnlyList<Notification>>> MarkReadAsync(
        IReadOnlyList<string> ids,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Notification>>.Failure(ApiErrors.Unauthorized());
        }
        if (ids is null || ids.Count == 0)
        {
            return RepositoryResult<IReadOnlyList<Notification>>.Failure(
                ApiErrors.Invalid("No notification ids provided."));
        }

        var notifications = await _dbContext.Notifications
            .Where(notification => ids.Contains(notification.Id)
                && notification.RecipientUserId == actorUserId)
            .ToListAsync();

        if (notifications.Count == 0)
        {
            return RepositoryResult<IReadOnlyList<Notification>>.Failure(
                ApiErrors.NotFound("Notifications"));
        }

        var now = DateTime.UtcNow;
        foreach (var notification in notifications)
        {
            notification.ReadAt ??= now;
            if (notification.Status == NotificationStatus.Pending)
            {
                notification.Status = NotificationStatus.Delivered;
            }
        }

        await _dbContext.SaveChangesAsync();
        return RepositoryResult<IReadOnlyList<Notification>>.Success(notifications);
    }

    public async Task<RepositoryResult<int>> GetUnreadCountAsync(
        string? communityId,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<int>.Failure(ApiErrors.Unauthorized());
        }

        var query = _dbContext.Notifications
            .Where(notification => notification.RecipientUserId == actorUserId
                && notification.ReadAt == null);

        if (!string.IsNullOrWhiteSpace(communityId))
        {
            query = query.Where(notification => notification.CommunityId == communityId);
        }

        var count = await query.CountAsync();
        return RepositoryResult<int>.Success(count);
    }
}
