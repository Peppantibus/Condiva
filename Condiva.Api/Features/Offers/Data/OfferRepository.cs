using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Results;
using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Offers.Data;

public sealed class OfferRepository : IOfferRepository
{
    public async Task<RepositoryResult<IReadOnlyList<Offer>>> GetAllAsync(
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<IReadOnlyList<Offer>>.Failure(ApiErrors.Unauthorized());
        }

        var offers = await dbContext.Offers
            .Include(offer => offer.Community)
            .Include(offer => offer.OffererUser)
            .Where(offer => dbContext.Memberships.Any(membership =>
                membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active
                && membership.CommunityId == offer.CommunityId))
            .ToListAsync();
        return RepositoryResult<IReadOnlyList<Offer>>.Success(offers);
    }

    public async Task<RepositoryResult<Offer>> GetByIdAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Unauthorized());
        }

        var offer = await dbContext.Offers
            .Include(item => item.Community)
            .Include(item => item.OffererUser)
            .FirstOrDefaultAsync(item => item.Id == id);
        return offer is null
            ? RepositoryResult<Offer>.Failure(ApiErrors.NotFound("Offer"))
            : await EnsureCommunityMemberAsync(offer.CommunityId, actorUserId, dbContext, offer);
    }

    public async Task<RepositoryResult<PagedResult<Offer>>> GetMineAsync(
        string? communityId,
        string? status,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<PagedResult<Offer>>.Failure(ApiErrors.Unauthorized());
        }

        var pageNumber = page.GetValueOrDefault(1);
        var size = pageSize.GetValueOrDefault(20);
        if (pageNumber <= 0 || size <= 0 || size > 100)
        {
            return RepositoryResult<PagedResult<Offer>>.Failure(
                ApiErrors.Invalid("Invalid pagination parameters."));
        }

        var query = dbContext.Offers
            .Include(offer => offer.Community)
            .Include(offer => offer.OffererUser)
            .AsQueryable()
            .Where(offer => offer.OffererUserId == actorUserId);

        if (!string.IsNullOrWhiteSpace(communityId))
        {
            query = query.Where(offer => offer.CommunityId == communityId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OfferStatus>(status, true, out var offerStatus))
            {
                return RepositoryResult<PagedResult<Offer>>.Failure(
                    ApiErrors.Invalid("Invalid status filter."));
            }
            query = query.Where(offer => offer.Status == offerStatus);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(offer => offer.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync();

        return RepositoryResult<PagedResult<Offer>>.Success(
            new PagedResult<Offer>(items, pageNumber, size, total));
    }

    public async Task<RepositoryResult<Offer>> CreateAsync(
        Offer body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Unauthorized());
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.OffererUserId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Required(nameof(body.OffererUserId)));
        }
        if (!string.Equals(body.OffererUserId, actorUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("OffererUserId must match the current user."));
        }
        if (string.IsNullOrWhiteSpace(body.ItemId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Required(nameof(body.ItemId)));
        }
        if (body.Status != OfferStatus.Open)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("Status must be Open on create."));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var offererExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.OffererUserId);
        if (!offererExists)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("OffererUserId does not exist."));
        }
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.OffererUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("OffererUserId is not a member of the community."));
        }
        var item = await dbContext.Items.FindAsync(body.ItemId);
        if (item is null)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("ItemId does not exist."));
        }
        if (item.CommunityId != body.CommunityId)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("ItemId does not belong to the community."));
        }
        if (!string.Equals(item.OwnerUserId, body.OffererUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("OffererUserId must match the item owner."));
        }
        if (!string.IsNullOrWhiteSpace(body.RequestId))
        {
            var request = await dbContext.Requests.FindAsync(body.RequestId);
            if (request is null)
            {
                return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("RequestId does not exist."));
            }
            if (request.CommunityId != body.CommunityId)
            {
                return RepositoryResult<Offer>.Failure(
                    ApiErrors.Invalid("RequestId does not belong to the community."));
            }
            if (request.Status != RequestStatus.Open)
            {
                return RepositoryResult<Offer>.Failure(
                    ApiErrors.Invalid("Request is not open."));
            }
        }
        if (string.IsNullOrWhiteSpace(body.Id))
        {
            body.Id = Guid.NewGuid().ToString();
        }
        body.CreatedAt = DateTime.UtcNow;

        dbContext.Offers.Add(body);
        await dbContext.SaveChangesAsync();
        var createdOffer = await dbContext.Offers
            .Include(offer => offer.Community)
            .Include(offer => offer.OffererUser)
            .FirstOrDefaultAsync(offer => offer.Id == body.Id);

        return RepositoryResult<Offer>.Success(createdOffer ?? body);
    }

    public async Task<RepositoryResult<Offer>> UpdateAsync(
        string id,
        Offer body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Unauthorized());
        }
        var offer = await dbContext.Offers.FindAsync(id);
        if (offer is null)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.NotFound("Offer"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == offer.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("User is not allowed to update the offer."));
        }
        if (offer.Status != OfferStatus.Open)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("Offer cannot be updated unless open."));
        }
        if (string.IsNullOrWhiteSpace(body.CommunityId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Required(nameof(body.CommunityId)));
        }
        if (string.IsNullOrWhiteSpace(body.OffererUserId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Required(nameof(body.OffererUserId)));
        }
        if (!CanManageCommunity(membership)
            && !string.Equals(body.OffererUserId, offer.OffererUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("OffererUserId cannot be changed."));
        }
        if (string.IsNullOrWhiteSpace(body.ItemId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Required(nameof(body.ItemId)));
        }
        if (body.Status != OfferStatus.Open)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("Status cannot be changed via update."));
        }
        var communityExists = await dbContext.Communities
            .AnyAsync(community => community.Id == body.CommunityId);
        if (!communityExists)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("CommunityId does not exist."));
        }
        var offererExists = await dbContext.Users
            .AnyAsync(user => user.Id == body.OffererUserId);
        if (!offererExists)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("OffererUserId does not exist."));
        }
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == body.CommunityId
            && membership.UserId == body.OffererUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("OffererUserId is not a member of the community."));
        }
        var item = await dbContext.Items.FindAsync(body.ItemId);
        if (item is null)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("ItemId does not exist."));
        }
        if (item.CommunityId != body.CommunityId)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("ItemId does not belong to the community."));
        }
        if (!string.Equals(item.OwnerUserId, body.OffererUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("OffererUserId must match the item owner."));
        }
        if (!string.IsNullOrWhiteSpace(body.RequestId))
        {
            var request = await dbContext.Requests.FindAsync(body.RequestId);
            if (request is null)
            {
                return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("RequestId does not exist."));
            }
            if (request.CommunityId != body.CommunityId)
            {
                return RepositoryResult<Offer>.Failure(
                    ApiErrors.Invalid("RequestId does not belong to the community."));
            }
            if (request.Status != RequestStatus.Open)
            {
                return RepositoryResult<Offer>.Failure(
                    ApiErrors.Invalid("Request is not open."));
            }
        }

        offer.CommunityId = body.CommunityId;
        offer.OffererUserId = body.OffererUserId;
        offer.RequestId = body.RequestId;
        offer.ItemId = body.ItemId;
        offer.Message = body.Message;
        offer.Status = body.Status;

        await dbContext.SaveChangesAsync();
        var updatedOffer = await dbContext.Offers
            .Include(foundOffer => foundOffer.Community)
            .Include(foundOffer => foundOffer.OffererUser)
            .FirstOrDefaultAsync(foundOffer => foundOffer.Id == offer.Id);

        return RepositoryResult<Offer>.Success(updatedOffer ?? offer);
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
        var offer = await dbContext.Offers.FindAsync(id);
        if (offer is null)
        {
            return RepositoryResult<bool>.Failure(ApiErrors.NotFound("Offer"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == offer.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("User is not allowed to delete the offer."));
        }
        if (offer.Status != OfferStatus.Open)
        {
            return RepositoryResult<bool>.Failure(
                ApiErrors.Invalid("Offer cannot be deleted unless open."));
        }

        dbContext.Offers.Remove(offer);
        await dbContext.SaveChangesAsync();
        return RepositoryResult<bool>.Success(true);
    }

    public async Task<RepositoryResult<Loan>> AcceptAsync(
        string id,
        AcceptOfferRequest body,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Unauthorized());
        }
        var offer = await dbContext.Offers.FindAsync(id);
        if (offer is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.NotFound("Offer"));
        }
        if (offer.Status != OfferStatus.Open)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Offer is not open."));
        }
        var item = await dbContext.Items.FindAsync(offer.ItemId);
        if (item is null)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Offer item does not exist."));
        }
        if (item.Status != ItemStatus.Available)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Item is not available."));
        }

        var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }

        string borrowerUserId;
        Request? request = null;
        if (!string.IsNullOrWhiteSpace(offer.RequestId))
        {
            request = await dbContext.Requests.FindAsync(offer.RequestId);
            if (request is null)
            {
                return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Request does not exist."));
            }
            if (request.Status != RequestStatus.Open)
            {
                return RepositoryResult<Loan>.Failure(ApiErrors.Invalid("Request is not open."));
            }
            if (request.RequesterUserId != actorUserId)
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("ActorUserId must be the requester."));
            }
            borrowerUserId = request.RequesterUserId;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(body.BorrowerUserId))
            {
                return RepositoryResult<Loan>.Failure(ApiErrors.Required(nameof(body.BorrowerUserId)));
            }
            if (body.BorrowerUserId != actorUserId)
            {
                return RepositoryResult<Loan>.Failure(
                    ApiErrors.Invalid("ActorUserId must match BorrowerUserId."));
            }
            borrowerUserId = body.BorrowerUserId;
        }

        var lenderMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == offer.CommunityId
            && membership.UserId == offer.OffererUserId
            && membership.Status == MembershipStatus.Active);
        if (!lenderMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("OffererUserId is not a member of the community."));
        }
        var borrowerMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == offer.CommunityId
            && membership.UserId == borrowerUserId
            && membership.Status == MembershipStatus.Active);
        if (!borrowerMember)
        {
            return RepositoryResult<Loan>.Failure(
                ApiErrors.Invalid("BorrowerUserId is not a member of the community."));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        offer.Status = OfferStatus.Accepted;
        item.Status = ItemStatus.Reserved;

        var loan = new Loan
        {
            Id = Guid.NewGuid().ToString(),
            CommunityId = offer.CommunityId,
            ItemId = offer.ItemId,
            LenderUserId = offer.OffererUserId,
            BorrowerUserId = borrowerUserId,
            RequestId = offer.RequestId,
            OfferId = offer.Id,
            Status = LoanStatus.Reserved,
            StartAt = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(offer.RequestId))
        {
            if (request is not null)
            {
                request.Status = RequestStatus.Accepted;
            }
        }

        var now = DateTime.UtcNow;
        var events = new List<Event>
        {
            CreateEvent(offer.CommunityId, actorUserId, "Offer", offer.Id, "OfferAccepted", now),
            CreateEvent(offer.CommunityId, actorUserId, "Item", item.Id, "ItemReserved", now),
            CreateEvent(offer.CommunityId, actorUserId, "Loan", loan.Id, "LoanReserved", now)
        };
        if (request is not null)
        {
            events.Add(CreateEvent(offer.CommunityId, actorUserId, "Request", request.Id, "RequestAccepted", now));
        }

        dbContext.Loans.Add(loan);
        dbContext.Events.AddRange(events);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        return RepositoryResult<Loan>.Success(loan);
    }

    public async Task<RepositoryResult<Offer>> RejectAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var offer = await dbContext.Offers.FindAsync(id);
        if (offer is null)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.NotFound("Offer"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == offer.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        if (offer.Status != OfferStatus.Open)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("Offer is not open."));
        }
        var canManage = CanManageCommunity(membership);
        if (!string.IsNullOrWhiteSpace(offer.RequestId))
        {
            var request = await dbContext.Requests.FindAsync(offer.RequestId);
            if (request is null)
            {
                return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("Request does not exist."));
            }
            if (!canManage
                && !string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal))
            {
                return RepositoryResult<Offer>.Failure(
                    ApiErrors.Invalid("User is not allowed to reject the offer."));
            }
        }
        else if (!canManage
            && !string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal))
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("User is not allowed to reject the offer."));
        }

        offer.Status = OfferStatus.Rejected;
        dbContext.Events.Add(CreateEvent(
            offer.CommunityId,
            actorUserId,
            "Offer",
            offer.Id,
            "OfferRejected",
            DateTime.UtcNow));
        await dbContext.SaveChangesAsync();
        return RepositoryResult<Offer>.Success(offer);
    }

    public async Task<RepositoryResult<Offer>> WithdrawAsync(
        string id,
        ClaimsPrincipal user,
        CondivaDbContext dbContext)
    {
        var actorUserId = CurrentUser.GetUserId(user);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Unauthorized());
        }
        var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
        if (!actorExists)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("ActorUserId does not exist."));
        }
        var offer = await dbContext.Offers.FindAsync(id);
        if (offer is null)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.NotFound("Offer"));
        }
        var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
            membership.CommunityId == offer.CommunityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (membership is null)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }
        var canManage = CanManageCommunity(membership)
            || string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal);
        if (!canManage)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("User is not allowed to withdraw the offer."));
        }
        if (offer.Status != OfferStatus.Open)
        {
            return RepositoryResult<Offer>.Failure(ApiErrors.Invalid("Offer is not open."));
        }

        offer.Status = OfferStatus.Withdrawn;
        dbContext.Events.Add(CreateEvent(
            offer.CommunityId,
            actorUserId,
            "Offer",
            offer.Id,
            "OfferWithdrawn",
            DateTime.UtcNow));
        await dbContext.SaveChangesAsync();
        return RepositoryResult<Offer>.Success(offer);
    }

    private static bool CanManageCommunity(Membership membership)
    {
        return membership.Role == MembershipRole.Owner
            || membership.Role == MembershipRole.Moderator;
    }

    private static async Task<RepositoryResult<Offer>> EnsureCommunityMemberAsync(
        string communityId,
        string actorUserId,
        CondivaDbContext dbContext,
        Offer offer)
    {
        var isMember = await dbContext.Memberships.AnyAsync(membership =>
            membership.CommunityId == communityId
            && membership.UserId == actorUserId
            && membership.Status == MembershipStatus.Active);
        if (!isMember)
        {
            return RepositoryResult<Offer>.Failure(
                ApiErrors.Invalid("User is not a member of the community."));
        }

        return RepositoryResult<Offer>.Success(offer);
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
