using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Reputations.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Loans.Data;

public sealed class LoanRepository : ILoanRepository
{
    private readonly CondivaDbContext _dbContext;

    public LoanRepository(CondivaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<RepositoryResult<PagedResult<Loan>>> GetAllAsync(
        string? communityId,
        string? status,
        DateTime? from,
        DateTime? to,
        int? page,
        int? pageSize,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Loan>>.Failure(ApiErrors.Unauthorized());
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            return RepositoryResult<PagedResult<Loan>>.Failure(
                ApiErrors.Invalid("Invalid date range."));
        }

        LoanStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<LoanStatus>(status, true, out var parsedStatus))
            {
                return RepositoryResult<PagedResult<Loan>>.Failure(
                    ApiErrors.Invalid("Invalid status filter."));
            }
            statusFilter = parsedStatus;
        }

        var query = _dbContext.Loans
            .Include(loan => loan.LenderUser)
            .Include(loan => loan.BorrowerUser)
            .Include(loan => loan.Item)
            .Where(loan => _dbContext.Memberships.Any(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active
                && membership.CommunityId == loan.CommunityId))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(communityId))
        {
            query = query.Where(loan => loan.CommunityId == communityId);
        }
        if (statusFilter.HasValue)
        {
            query = query.Where(loan => loan.Status == statusFilter.Value);
        }
        if (from.HasValue)
        {
            query = query.Where(loan => loan.StartAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(loan => loan.StartAt <= to.Value);
        }

        var usePaging = page.HasValue || pageSize.HasValue;
        if (usePaging)
        {
            var pageNumber = page.GetValueOrDefault(1);
            var size = pageSize.GetValueOrDefault(20);
            if (pageNumber <= 0 || size <= 0 || size > 100)
            {
                return RepositoryResult<PagedResult<Loan>>.Failure(
                    ApiErrors.Invalid("Invalid pagination values."));
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(loan => loan.StartAt)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();
            return RepositoryResult<PagedResult<Loan>>.Success(
                new PagedResult<Loan>(items, pageNumber, size, total));
        }

        var loans = await query.ToListAsync();
        return RepositoryResult<PagedResult<Loan>>.Success(
            new PagedResult<Loan>(loans, 1, loans.Count, loans.Count));
    }

    public async Task<RepositoryResult<Loan>> GetByIdAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }

        var loan = await _dbContext.Loans
            .Include(candidate => candidate.LenderUser)
            .Include(candidate => candidate.BorrowerUser)
            .Include(candidate => candidate.Item)
            .FirstOrDefaultAsync(candidate => candidate.Id == id);
        return loan is null
            ? RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"))
            : await EnsureCommunityMemberAsync(loan.CommunityId, actorUserId, loan);
    }

    public async Task<RepositoryResult<Loan>> CreateAsync(
        Loan body,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.ItemId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.ItemId)));
        }
        if (string.IsNullOrWhiteSpace(body.LenderUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.LenderUserId)));
        }
        if (string.IsNullOrWhiteSpace(body.BorrowerUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.BorrowerUserId)));
        }
        if (!string.Equals(body.LenderUserId, actorUserId, StringComparison.Ordinal)
            && !string.Equals(body.BorrowerUserId, actorUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to create the loan."));
        }
        if (body.Status != LoanStatus.Reserved)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Status must be Reserved on create."));
        }
        var invariantError = ValidateReturnInvariant(
            body.Status,
            body.ReturnedAt,
            body.ReturnRequestedAt,
            body.ReturnConfirmedAt);
        if (invariantError is not null)
        {
            return RepositoryResult<Loan>.Failure(invariantError);
        }
        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var actorMembership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (actorMembership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        if (!CanManageCommunity(actorMembership))
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to create the loan directly."));
        }
        var item = await _dbContext.Items.FindAsync(body.ItemId);
        if (item is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ItemId does not exist."));
        }
        if (item.CommunityId != body.CommunityId)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("ItemId does not belong to the community."));
        }
        if (item.Status != ItemStatus.Available)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Item is not available."));
        }
        if (!string.Equals(item.OwnerUserId, body.LenderUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("LenderUserId must match the item owner."));
        }
        var lenderExists = await _dbContext.Users
            .AnyAsync(user => user.Id == body.LenderUserId);
        if (!lenderExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("LenderUserId does not exist."));
        }
        var borrowerExists = await _dbContext.Users
            .AnyAsync(user => user.Id == body.BorrowerUserId);
        if (!borrowerExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("BorrowerUserId does not exist."));
        }
        var lenderMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.LenderUserId
            && membership.Status == MembershipStatus.Active);
        if (!lenderMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("LenderUserId is not a member of the community."));
        }
        var borrowerMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.BorrowerUserId
            && membership.Status == MembershipStatus.Active);
        if (!borrowerMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("BorrowerUserId is not a member of the community."));
        }
        if (!string.IsNullOrWhiteSpace(body.RequestId))
        {
            var request = await _dbContext.Requests.FindAsync(body.RequestId);
            if (request is null)
            {
                return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("RequestId does not exist."));
            }
            if (request.CommunityId != body.CommunityId)
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("RequestId does not belong to the community."));
            }
        }
        if (!string.IsNullOrWhiteSpace(body.OfferId))
        {
            var offer = await _dbContext.Offers.FindAsync(body.OfferId);
            if (offer is null)
            {
                return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("OfferId does not exist."));
            }
            if (offer.CommunityId != body.CommunityId)
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("OfferId does not belong to the community."));
            }
        }
        if (string.IsNullOrWhiteSpace(body.Id))
        {
            body.Id = Guid.NewGuid().ToString();
        }
        if (body.StartAt == default)
        {
            body.StartAt = DateTime.UtcNow;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        item.Status = ItemStatus.Reserved;
        if (!string.IsNullOrWhiteSpace(body.RequestId))
        {
            var request = await _dbContext.Requests.FindAsync(body.RequestId);
            if (request is not null)
            {
                request.Status = RequestStatus.Accepted;
            }
        }

        _dbContext.Loans.Add(body);
        await _dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return RepositoryResult<Loan>.Success(body);
    }

    public async Task<RepositoryResult<Loan>> UpdateAsync(
        string id,
        Loan body,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var loan = await _dbContext.Loans.FindAsync(id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to update the loan."));
        }
        if (loan.Status != LoanStatus.Reserved)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("Loan cannot be updated unless reserved."));
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.ItemId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.ItemId)));
        }
        if (string.IsNullOrWhiteSpace(body.LenderUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.LenderUserId)));
        }
        if (string.IsNullOrWhiteSpace(body.BorrowerUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.BorrowerUserId)));
        }
        if (body.Status != LoanStatus.Reserved)
        {
            if (body.Status == LoanStatus.Returned)
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("Use POST /api/loans/{id}/return to mark a loan as returned."));
            }
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Status cannot be changed via update."));
        }
        if (body.ReturnedAt is not null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("ReturnedAt cannot be set via update."));
        }
        if (body.StartAt != default)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("StartAt cannot be changed via update."));
        }
        if (body.DueAt is not null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("DueAt cannot be changed via update."));
        }
        var invariantError = ValidateReturnInvariant(
            body.Status,
            body.ReturnedAt,
            body.ReturnRequestedAt,
            body.ReturnConfirmedAt);
        if (invariantError is not null)
        {
            return RepositoryResult<Loan>.Failure(invariantError);
        }
        if (!CanManageCommunity(membership))
        {
            if (!string.Equals(body.CommunityId, loan.CommunityId, StringComparison.Ordinal)
                || !string.Equals(body.ItemId, loan.ItemId, StringComparison.Ordinal)
                || !string.Equals(body.LenderUserId, loan.LenderUserId, StringComparison.Ordinal)
                || !string.Equals(body.BorrowerUserId, loan.BorrowerUserId, StringComparison.Ordinal)
                || !string.Equals(body.RequestId, loan.RequestId, StringComparison.Ordinal)
                || !string.Equals(body.OfferId, loan.OfferId, StringComparison.Ordinal))
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("Only community managers can change loan participants or references."));
            }
        }
        var communityExists = await _dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var item = await _dbContext.Items.FindAsync(body.ItemId);
        if (item is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ItemId does not exist."));
        }
        if (item.CommunityId != body.CommunityId)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("ItemId does not belong to the community."));
        }
        if (!string.Equals(item.OwnerUserId, body.LenderUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("LenderUserId must match the item owner."));
        }
        var lenderExists = await _dbContext.Users
            .AnyAsync(user => user.Id == body.LenderUserId);
        if (!lenderExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("LenderUserId does not exist."));
        }
        var borrowerExists = await _dbContext.Users
            .AnyAsync(user => user.Id == body.BorrowerUserId);
        if (!borrowerExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("BorrowerUserId does not exist."));
        }
        var lenderMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.LenderUserId
            && membership.Status == MembershipStatus.Active);
        if (!lenderMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("LenderUserId is not a member of the community."));
        }
        var borrowerMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.BorrowerUserId
            && membership.Status == MembershipStatus.Active);
        if (!borrowerMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("BorrowerUserId is not a member of the community."));
        }
        if (!string.IsNullOrWhiteSpace(body.RequestId))
        {
            var request = await _dbContext.Requests.FindAsync(body.RequestId);
            if (request is null)
            {
                return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("RequestId does not exist."));
            }
            if (request.CommunityId != body.CommunityId)
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("RequestId does not belong to the community."));
            }
        }
        if (!string.IsNullOrWhiteSpace(body.OfferId))
        {
            var offer = await _dbContext.Offers.FindAsync(body.OfferId);
            if (offer is null)
            {
                return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("OfferId does not exist."));
            }
            if (offer.CommunityId != body.CommunityId)
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("OfferId does not belong to the community."));
            }
        }

        loan.CommunityId = body.CommunityId;
        loan.ItemId = body.ItemId;
        loan.LenderUserId = body.LenderUserId;
        loan.BorrowerUserId = body.BorrowerUserId;
        loan.RequestId = body.RequestId;
        loan.OfferId = body.OfferId;
        loan.Status = body.Status;

        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    public async Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        var loan = await _dbContext.Loans.FindAsync(id);
        if (loan is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Forbidden("User is not allowed to delete the loan."));
        }
        if (loan.Status != LoanStatus.Reserved)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("Loan cannot be deleted unless reserved."));
        }

        _dbContext.Loans.Remove(loan);
        await _dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    public async Task<RepositoryResult<Loan>> StartAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await _dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var loan = await _dbContext.Loans.FindAsync(id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to start the loan."));
        }
        if (loan.Status != LoanStatus.Reserved)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Loan is not reserved."));
        }

        var item = await _dbContext.Items.FindAsync(loan.ItemId);
        if (item is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Item does not exist."));
        }
        if (item.Status != ItemStatus.Reserved)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Item is not reserved."));
        }

        loan.Status = LoanStatus.InLoan;
        item.Status = ItemStatus.InLoan;
        if (loan.StartAt == default)
        {
            loan.StartAt = DateTime.UtcNow;
        }

        _dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Loan",
            loan.Id,
            "LoanStarted",
            DateTime.UtcNow));
        _dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Item",
            item.Id,
            "ItemInLoan",
            DateTime.UtcNow));
        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    public async Task<RepositoryResult<Loan>> ReturnRequestAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await _dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var loan = await _dbContext.Loans
            .Include(candidate => candidate.LenderUser)
            .Include(candidate => candidate.BorrowerUser)
            .FirstOrDefaultAsync(candidate => candidate.Id == id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to request the return."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to request the return."));
        }
        if (loan.Status != LoanStatus.InLoan)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Conflict("Loan is not in progress."));
        }

        loan.Status = LoanStatus.ReturnRequested;
        loan.ReturnRequestedAt = DateTime.UtcNow;
        loan.ReturnConfirmedAt = null;

        var invariantError = ValidateReturnInvariant(
            loan.Status,
            loan.ReturnedAt,
            loan.ReturnRequestedAt,
            loan.ReturnConfirmedAt);
        if (invariantError is not null)
        {
            return RepositoryResult<Loan>.Failure(invariantError);
        }

        _dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Loan",
            loan.Id,
            "LoanReturnRequested",
            DateTime.UtcNow));

        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    public async Task<RepositoryResult<Loan>> ReturnConfirmAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await _dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var loan = await _dbContext.Loans
            .Include(candidate => candidate.LenderUser)
            .Include(candidate => candidate.BorrowerUser)
            .FirstOrDefaultAsync(candidate => candidate.Id == id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to confirm the return."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to confirm the return."));
        }
        if (loan.Status != LoanStatus.ReturnRequested)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Conflict("Loan is not waiting for return confirmation."));
        }

        var item = await _dbContext.Items.FindAsync(loan.ItemId);
        if (item is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Item does not exist."));
        }

        loan.Status = LoanStatus.Returned;
        loan.ReturnedAt = DateTime.UtcNow;
        loan.ReturnConfirmedAt = DateTime.UtcNow;
        item.Status = ItemStatus.Available;

        var returnedOnTime = loan.DueAt != null
            && loan.ReturnedAt != null
            && loan.ReturnedAt <= loan.DueAt;

        var invariantError = ValidateReturnInvariant(
            loan.Status,
            loan.ReturnedAt,
            loan.ReturnRequestedAt,
            loan.ReturnConfirmedAt);
        if (invariantError is not null)
        {
            return RepositoryResult<Loan>.Failure(invariantError);
        }

        await ApplyReturnReputationUpdate(
            loan.CommunityId,
            loan.LenderUserId,
            loan.BorrowerUserId,
            returnedOnTime);

        _dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Loan",
            loan.Id,
            "LoanReturned",
            DateTime.UtcNow));
        _dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Item",
            item.Id,
            "ItemAvailable",
            DateTime.UtcNow));
        if (!string.IsNullOrWhiteSpace(loan.RequestId))
        {
            var request = await _dbContext.Requests.FindAsync(loan.RequestId);
            if (request is not null)
            {
                request.Status = RequestStatus.Closed;
                _dbContext.Events.Add(CreateEvent(
                    loan.CommunityId,
                    actorUserId,
                    "Request",
                    request.Id,
                    "RequestClosed",
                    DateTime.UtcNow));
            }
        }

        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    public async Task<RepositoryResult<Loan>> ReturnCancelAsync(
        string id,
        ClaimsPrincipal user)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await _dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var loan = await _dbContext.Loans
            .Include(candidate => candidate.LenderUser)
            .Include(candidate => candidate.BorrowerUser)
            .FirstOrDefaultAsync(candidate => candidate.Id == id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await _dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to cancel the return request."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Forbidden("User is not allowed to cancel the return request."));
        }
        if (loan.Status != LoanStatus.ReturnRequested)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Conflict("Loan is not waiting for return confirmation."));
        }

        loan.Status = LoanStatus.InLoan;
        loan.ReturnRequestedAt = null;
        loan.ReturnConfirmedAt = null;

        var invariantError = ValidateReturnInvariant(
            loan.Status,
            loan.ReturnedAt,
            loan.ReturnRequestedAt,
            loan.ReturnConfirmedAt);
        if (invariantError is not null)
        {
            return RepositoryResult<Loan>.Failure(invariantError);
        }

        _dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Loan",
            loan.Id,
            "LoanReturnCanceled",
            DateTime.UtcNow));

        await _dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    private async Task ApplyReturnReputationUpdate(
        string communityId,
        string lenderUserId,
        string borrowerUserId,
        bool returnedOnTime)
    {
        var lenderProfile = await GetOrCreateProfile(communityId, lenderUserId);
        lenderProfile.LendCount += 1;
        lenderProfile.Score += ReputationWeights.LendPoints;
        lenderProfile.UpdatedAt = DateTime.UtcNow;

        var borrowerProfile = await GetOrCreateProfile(communityId, borrowerUserId);
        borrowerProfile.ReturnCount += 1;
        borrowerProfile.Score += ReputationWeights.ReturnPoints;
        if (returnedOnTime)
        {
            borrowerProfile.OnTimeReturnCount += 1;
            borrowerProfile.Score += ReputationWeights.OnTimeReturnBonus;
        }
        borrowerProfile.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<ReputationProfile> GetOrCreateProfile(
        string communityId,
        string userId)
    {
        var profile = await _dbContext.Reputations.FirstOrDefaultAsync(reputation =>
            reputation.CommunityId == communityId && reputation.UserId == userId);

        if (profile is not null)
        {
            return profile;
        }

        profile = new ReputationProfile
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            UserId = userId,
            Score = 0,
            LendCount = 0,
            ReturnCount = 0,
            OnTimeReturnCount = 0,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Reputations.Add(profile);
        return profile;
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }

    private async Task<RepositoryResult<Loan>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        Loan loan)
    {
        var isMember = await _dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        return RepositoryResult<Loan>.Success(loan);
    }

    private static IResult? ValidateReturnInvariant(
        LoanStatus status,
        DateTime? returnedAt,
        DateTime? returnRequestedAt,
        DateTime? returnConfirmedAt)
    {
        if (status == LoanStatus.Returned && returnedAt is null)
        {
            return ApiErrors.Invalid("ReturnedAt is required when status is Returned.");
        }
        if (status != LoanStatus.Returned && returnedAt is not null)
        {
            return ApiErrors.Invalid("ReturnedAt can only be set when status is Returned.");
        }
        if (returnConfirmedAt is not null && status != LoanStatus.Returned)
        {
            return ApiErrors.Invalid("ReturnConfirmedAt can only be set when status is Returned.");
        }
        if (returnConfirmedAt is not null && returnedAt is null)
        {
            return ApiErrors.Invalid("ReturnConfirmedAt requires ReturnedAt.");
        }
        if (status == LoanStatus.ReturnRequested && returnRequestedAt is null)
        {
            return ApiErrors.Invalid("ReturnRequestedAt is required when status is ReturnRequested.");
        }
        if (status != LoanStatus.ReturnRequested
            && status != LoanStatus.Returned
            && returnRequestedAt is not null)
        {
            return ApiErrors.Invalid("ReturnRequestedAt can only be set when status is ReturnRequested.");
        }

        return null;
    }

    private static Event CreateEvent(
        string communityId,
        string actorUserId,
        string entityType,
        string entityId,
        string action,
        DateTime createdAt)
    {
        return new Event
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = communityId,
            ActorUserId = actorUserId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            CreatedAt = createdAt
        };
    }
}
