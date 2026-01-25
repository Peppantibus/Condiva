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
    public async Task<RepositoryResult<IReadOnlyList<Loan>>> GetAllAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Loan>>.Failure(ApiErrors.Unauthorized());
        }

        var loans = await dbContext.Loans
            .Join(
                dbContext.Memberships.Where(membership =>
                    membership.UserId == actorUserId
                    && membership.Status == MembershipStatus.Active),
                loan => loan.CommunityId,
                membership => membership.CommunityId,
                (loan, _) => loan)
            .Distinct()
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Loan>>.Success(loans);
    }

    public async Task<RepositoryResult<Loan>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }

        var loan = await dbContext.Loans.FindAsync(id);
        return loan is null
            ? RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"))
            : await EnsureCommunityMemberAsync(loan.CommunityId, actorUserId, dbContext, loan);
    }

    public async Task<RepositoryResult<Loan>> CreateAsync(
        Loan body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
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
                ApiErrors.Invalid("User is not allowed to create the loan."));
        }
        if (body.Status != LoanStatus.Reserved)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Status must be Reserved on create."));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var actorMembership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (actorMembership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        if (!CanManageCommunity(actorMembership))
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not allowed to create the loan directly."));
        }
        var item = await dbContext.Items.FindAsync(body.ItemId);
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
        var lenderExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.LenderUserId);
        if (!lenderExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("LenderUserId does not exist."));
        }
        var borrowerExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.BorrowerUserId);
        if (!borrowerExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("BorrowerUserId does not exist."));
        }
        var lenderMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.LenderUserId
            && membership.Status == MembershipStatus.Active);
        if (!lenderMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("LenderUserId is not a member of the community."));
        }
        var borrowerMember = await dbContext.Memberships.AnyAsync(membership =>
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
            var request = await dbContext.Requests.FindAsync(body.RequestId);
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
            var offer = await dbContext.Offers.FindAsync(body.OfferId);
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        item.Status = ItemStatus.Reserved;
        if (!string.IsNullOrWhiteSpace(body.RequestId))
        {
            var request = await dbContext.Requests.FindAsync(body.RequestId);
            if (request is not null)
            {
                request.Status = RequestStatus.Accepted;
            }
        }

        dbContext.Loans.Add(body);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return RepositoryResult<Loan>.Success(body);
    }

    public async Task<RepositoryResult<Loan>> UpdateAsync(
        string id,
        Loan body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var loan = await dbContext.Loans.FindAsync(id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not allowed to update the loan."));
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
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Status cannot be changed via update."));
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
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var item = await dbContext.Items.FindAsync(body.ItemId);
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
        var lenderExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.LenderUserId);
        if (!lenderExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("LenderUserId does not exist."));
        }
        var borrowerExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.BorrowerUserId);
        if (!borrowerExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("BorrowerUserId does not exist."));
        }
        var lenderMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.LenderUserId
            && membership.Status == MembershipStatus.Active);
        if (!lenderMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("LenderUserId is not a member of the community."));
        }
        var borrowerMember = await dbContext.Memberships.AnyAsync(membership =>
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
            var request = await dbContext.Requests.FindAsync(body.RequestId);
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
            var offer = await dbContext.Offers.FindAsync(body.OfferId);
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
        loan.StartAt = body.StartAt;
        loan.DueAt = body.DueAt;
        loan.ReturnedAt = body.ReturnedAt;

        await dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    public async Task<RepositoryResult<bool>> DeleteAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<bool>.Failure(ApiErrors.Unauthorized());
        }
        var loan = await dbContext.Loans.FindAsync(id);
        if (loan is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not allowed to delete the loan."));
        }
        if (loan.Status != LoanStatus.Reserved)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("Loan cannot be deleted unless reserved."));
        }

        dbContext.Loans.Remove(loan);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    public async Task<RepositoryResult<Loan>> StartAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var loan = await dbContext.Loans.FindAsync(id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not allowed to start the loan."));
        }
        if (loan.Status != LoanStatus.Reserved)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Loan is not reserved."));
        }

        var item = await dbContext.Items.FindAsync(loan.ItemId);
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

        dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Loan",
            loan.Id,
            "LoanStarted",
            DateTime.UtcNow));
        dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Item",
            item.Id,
            "ItemInLoan",
            DateTime.UtcNow));
        await dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    public async Task<RepositoryResult<Loan>> ReturnAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var loan = await dbContext.Loans.FindAsync(id);
        if (loan is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Loan"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == loan.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
            || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("User is not allowed to return the loan."));
        }
        if (loan.Status != LoanStatus.InLoan)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Loan is not in progress."));
        }

        var item = await dbContext.Items.FindAsync(loan.ItemId);
        if (item is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Item does not exist."));
        }
        if (item.Status != ItemStatus.InLoan)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Item is not in loan."));
        }

        loan.Status = LoanStatus.Returned;
        loan.ReturnedAt = DateTime.UtcNow;
        item.Status = ItemStatus.Available;

        var returnedOnTime = loan.DueAt != null
            && loan.ReturnedAt != null
            && loan.ReturnedAt <= loan.DueAt;

        await ApplyReturnReputationUpdate(
            loan.CommunityId,
            loan.LenderUserId,
            loan.BorrowerUserId,
            returnedOnTime,
            dbContext);

        dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Loan",
            loan.Id,
            "LoanReturned",
            DateTime.UtcNow));
        dbContext.Events.Add(CreateEvent(
            loan.CommunityId,
            actorUserId,
            "Item",
            item.Id,
            "ItemAvailable",
            DateTime.UtcNow));
        if (!string.IsNullOrWhiteSpace(loan.RequestId))
        {
            var request = await dbContext.Requests.FindAsync(loan.RequestId);
            if (request is not null)
            {
                request.Status = RequestStatus.Closed;
                dbContext.Events.Add(CreateEvent(
                    loan.CommunityId,
                    actorUserId,
                    "Request",
                    request.Id,
                    "RequestClosed",
                    DateTime.UtcNow));
            }
        }

        await dbContext.SaveChangesAsync();
        return RepositoryResult<Loan>.Success(loan);
    }

    private static async Task ApplyReturnReputationUpdate(
        string communityId,
        string lenderUserId,
        string borrowerUserId,
        bool returnedOnTime,
        CondivaDbContext dbContext)
    {
        var lenderProfile = await GetOrCreateProfile(communityId, lenderUserId, dbContext);
        lenderProfile.LendCount += 1;
        lenderProfile.Score += ReputationWeights.LendPoints;
        lenderProfile.UpdatedAt = DateTime.UtcNow;

        var borrowerProfile = await GetOrCreateProfile(communityId, borrowerUserId, dbContext);
        borrowerProfile.ReturnCount += 1;
        borrowerProfile.Score += ReputationWeights.ReturnPoints;
        if (returnedOnTime)
        {
            borrowerProfile.OnTimeReturnCount += 1;
            borrowerProfile.Score += ReputationWeights.OnTimeReturnBonus;
        }
        borrowerProfile.UpdatedAt = DateTime.UtcNow;
    }

    private static async Task<ReputationProfile> GetOrCreateProfile(
        string communityId,
        string userId,
        CondivaDbContext dbContext)
    {
        var profile = await dbContext.Reputations.FirstOrDefaultAsync(reputation =>
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

        dbContext.Reputations.Add(profile);
        return profile;
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }

    private static async Task<RepositoryResult<Loan>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        CondivaDbContext dbContext,
        Loan loan)
    {
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
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
