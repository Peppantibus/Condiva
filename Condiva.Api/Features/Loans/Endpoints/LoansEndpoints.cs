using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Requests.Models;
using Condiva.Api.Features.Reputations.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Loans.Endpoints;

public static class LoansEndpoints
{
    public static IEndpointRouteBuilder MapLoansEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/loans");
        group.RequireAuthorization();
        group.WithTags("Loans");

        group.MapGet("/", async (CondivaDbContext dbContext) =>
            await dbContext.Loans.ToListAsync());

        group.MapGet("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var loan = await dbContext.Loans.FindAsync(id);
            return loan is null ? ApiErrors.NotFound("Loan") : Results.Ok(loan);
        });

        group.MapPost("/", async (
            Loan body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(body.ItemId))
            {
                return ApiErrors.Required(nameof(body.ItemId));
            }
            if (string.IsNullOrWhiteSpace(body.LenderUserId))
            {
                return ApiErrors.Required(nameof(body.LenderUserId));
            }
            if (string.IsNullOrWhiteSpace(body.BorrowerUserId))
            {
                return ApiErrors.Required(nameof(body.BorrowerUserId));
            }
            if (!string.Equals(body.LenderUserId, actorUserId, StringComparison.Ordinal)
                && !string.Equals(body.BorrowerUserId, actorUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("User is not allowed to create the loan.");
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var item = await dbContext.Items.FindAsync(body.ItemId);
            if (item is null)
            {
                return ApiErrors.Invalid("ItemId does not exist.");
            }
            if (item.CommunityId != body.CommunityId)
            {
                return ApiErrors.Invalid("ItemId does not belong to the community.");
            }
            var lenderExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.LenderUserId);
            if (!lenderExists)
            {
                return ApiErrors.Invalid("LenderUserId does not exist.");
            }
            var borrowerExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.BorrowerUserId);
            if (!borrowerExists)
            {
                return ApiErrors.Invalid("BorrowerUserId does not exist.");
            }
            var lenderMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.LenderUserId
                && membership.Status == MembershipStatus.Active);
            if (!lenderMember)
            {
                return ApiErrors.Invalid("LenderUserId is not a member of the community.");
            }
            var borrowerMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.BorrowerUserId
                && membership.Status == MembershipStatus.Active);
            if (!borrowerMember)
            {
                return ApiErrors.Invalid("BorrowerUserId is not a member of the community.");
            }
            if (!string.IsNullOrWhiteSpace(body.RequestId))
            {
                var request = await dbContext.Requests.FindAsync(body.RequestId);
                if (request is null)
                {
                    return ApiErrors.Invalid("RequestId does not exist.");
                }
                if (request.CommunityId != body.CommunityId)
                {
                    return ApiErrors.Invalid("RequestId does not belong to the community.");
                }
            }
            if (!string.IsNullOrWhiteSpace(body.OfferId))
            {
                var offer = await dbContext.Offers.FindAsync(body.OfferId);
                if (offer is null)
                {
                    return ApiErrors.Invalid("OfferId does not exist.");
                }
                if (offer.CommunityId != body.CommunityId)
                {
                    return ApiErrors.Invalid("OfferId does not belong to the community.");
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

            dbContext.Loans.Add(body);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/loans/{body.Id}", body);
        });

        group.MapPut("/{id}", async (
            string id,
            Loan body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var loan = await dbContext.Loans.FindAsync(id);
            if (loan is null)
            {
                return ApiErrors.NotFound("Loan");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == loan.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
                || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to update the loan.");
            }
            if (loan.Status != LoanStatus.Reserved)
            {
                return ApiErrors.Invalid("Loan cannot be updated unless reserved.");
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(body.ItemId))
            {
                return ApiErrors.Required(nameof(body.ItemId));
            }
            if (string.IsNullOrWhiteSpace(body.LenderUserId))
            {
                return ApiErrors.Required(nameof(body.LenderUserId));
            }
            if (string.IsNullOrWhiteSpace(body.BorrowerUserId))
            {
                return ApiErrors.Required(nameof(body.BorrowerUserId));
            }
            if (body.Status != LoanStatus.Reserved)
            {
                return ApiErrors.Invalid("Status cannot be changed via update.");
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var item = await dbContext.Items.FindAsync(body.ItemId);
            if (item is null)
            {
                return ApiErrors.Invalid("ItemId does not exist.");
            }
            if (item.CommunityId != body.CommunityId)
            {
                return ApiErrors.Invalid("ItemId does not belong to the community.");
            }
            var lenderExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.LenderUserId);
            if (!lenderExists)
            {
                return ApiErrors.Invalid("LenderUserId does not exist.");
            }
            var borrowerExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.BorrowerUserId);
            if (!borrowerExists)
            {
                return ApiErrors.Invalid("BorrowerUserId does not exist.");
            }
            var lenderMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.LenderUserId
                && membership.Status == MembershipStatus.Active);
            if (!lenderMember)
            {
                return ApiErrors.Invalid("LenderUserId is not a member of the community.");
            }
            var borrowerMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.BorrowerUserId
                && membership.Status == MembershipStatus.Active);
            if (!borrowerMember)
            {
                return ApiErrors.Invalid("BorrowerUserId is not a member of the community.");
            }
            if (!string.IsNullOrWhiteSpace(body.RequestId))
            {
                var request = await dbContext.Requests.FindAsync(body.RequestId);
                if (request is null)
                {
                    return ApiErrors.Invalid("RequestId does not exist.");
                }
                if (request.CommunityId != body.CommunityId)
                {
                    return ApiErrors.Invalid("RequestId does not belong to the community.");
                }
            }
            if (!string.IsNullOrWhiteSpace(body.OfferId))
            {
                var offer = await dbContext.Offers.FindAsync(body.OfferId);
                if (offer is null)
                {
                    return ApiErrors.Invalid("OfferId does not exist.");
                }
                if (offer.CommunityId != body.CommunityId)
                {
                    return ApiErrors.Invalid("OfferId does not belong to the community.");
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
            return Results.Ok(loan);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var loan = await dbContext.Loans.FindAsync(id);
            if (loan is null)
            {
                return ApiErrors.NotFound("Loan");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == loan.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
                || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to delete the loan.");
            }
            if (loan.Status != LoanStatus.Reserved)
            {
                return ApiErrors.Invalid("Loan cannot be deleted unless reserved.");
            }

            dbContext.Loans.Remove(loan);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id}/start", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
            if (!actorExists)
            {
                return ApiErrors.Invalid("ActorUserId does not exist.");
            }
            var loan = await dbContext.Loans.FindAsync(id);
            if (loan is null)
            {
                return ApiErrors.NotFound("Loan");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == loan.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
                || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to start the loan.");
            }
            if (loan.Status != LoanStatus.Reserved)
            {
                return ApiErrors.Invalid("Loan is not reserved.");
            }

            var item = await dbContext.Items.FindAsync(loan.ItemId);
            if (item is null)
            {
                return ApiErrors.Invalid("Item does not exist.");
            }
            if (item.Status != ItemStatus.Reserved)
            {
                return ApiErrors.Invalid("Item is not reserved.");
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
            return Results.Ok(loan);
        });

        group.MapPost("/{id}/return", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
            if (!actorExists)
            {
                return ApiErrors.Invalid("ActorUserId does not exist.");
            }
            var loan = await dbContext.Loans.FindAsync(id);
            if (loan is null)
            {
                return ApiErrors.NotFound("Loan");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == loan.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(loan.LenderUserId, actorUserId, StringComparison.Ordinal)
                || string.Equals(loan.BorrowerUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to return the loan.");
            }
            if (loan.Status != LoanStatus.InLoan)
            {
                return ApiErrors.Invalid("Loan is not in progress.");
            }

            var item = await dbContext.Items.FindAsync(loan.ItemId);
            if (item is null)
            {
                return ApiErrors.Invalid("Item does not exist.");
            }
            if (item.Status != ItemStatus.InLoan)
            {
                return ApiErrors.Invalid("Item is not in loan.");
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
            return Results.Ok(loan);
        });

        return endpoints;
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
