using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Errors;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Events.Models;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Offers.Endpoints;

public static class OffersEndpoints
{
    public static IEndpointRouteBuilder MapOffersEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/offers");
        group.RequireAuthorization();
        group.WithTags("Offers");

        group.MapGet("/", async (CondivaDbContext dbContext) =>
            await dbContext.Offers.ToListAsync());

        group.MapGet("/{id}", async (string id, CondivaDbContext dbContext) =>
        {
            var offer = await dbContext.Offers.FindAsync(id);
            return offer is null ? ApiErrors.NotFound("Offer") : Results.Ok(offer);
        });

        group.MapGet("/me", async (
            string? communityId,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var pageNumber = page.GetValueOrDefault(1);
            var size = pageSize.GetValueOrDefault(20);
            if (pageNumber <= 0 || size <= 0 || size > 100)
            {
                return ApiErrors.Invalid("Invalid pagination parameters.");
            }

            var query = dbContext.Offers.AsQueryable()
                .Where(offer => offer.OffererUserId == actorUserId);

            if (!string.IsNullOrWhiteSpace(communityId))
            {
                query = query.Where(offer => offer.CommunityId == communityId);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<OfferStatus>(status, true, out var offerStatus))
                {
                    return ApiErrors.Invalid("Invalid status filter.");
                }
                query = query.Where(offer => offer.Status == offerStatus);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(offer => offer.CreatedAt)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();

            return Results.Ok(new
            {
                items,
                page = pageNumber,
                pageSize = size,
                total
            });
        });

        group.MapPost("/", async (
            Offer body,
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
            if (string.IsNullOrWhiteSpace(body.OffererUserId))
            {
                return ApiErrors.Required(nameof(body.OffererUserId));
            }
            if (!string.Equals(body.OffererUserId, actorUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("OffererUserId must match the current user.");
            }
            if (string.IsNullOrWhiteSpace(body.ItemId))
            {
                return ApiErrors.Required(nameof(body.ItemId));
            }
            if (body.Status != OfferStatus.Open)
            {
                return ApiErrors.Invalid("Status must be Open on create.");
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var offererExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.OffererUserId);
            if (!offererExists)
            {
                return ApiErrors.Invalid("OffererUserId does not exist.");
            }
            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.OffererUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("OffererUserId is not a member of the community.");
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
            if (string.IsNullOrWhiteSpace(body.Id))
            {
                body.Id = Guid.NewGuid().ToString();
            }
            body.CreatedAt = DateTime.UtcNow;

            dbContext.Offers.Add(body);
            await dbContext.SaveChangesAsync();
            return Results.Created($"/api/offers/{body.Id}", body);
        });

        group.MapPut("/{id}", async (
            string id,
            Offer body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var offer = await dbContext.Offers.FindAsync(id);
            if (offer is null)
            {
                return ApiErrors.NotFound("Offer");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == offer.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to update the offer.");
            }
            if (offer.Status != OfferStatus.Open)
            {
                return ApiErrors.Invalid("Offer cannot be updated unless open.");
            }
            if (string.IsNullOrWhiteSpace(body.CommunityId))
            {
                return ApiErrors.Required(nameof(body.CommunityId));
            }
            if (string.IsNullOrWhiteSpace(body.OffererUserId))
            {
                return ApiErrors.Required(nameof(body.OffererUserId));
            }
            if (!CanManageCommunity(membership)
                && !string.Equals(body.OffererUserId, offer.OffererUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("OffererUserId cannot be changed.");
            }
            if (string.IsNullOrWhiteSpace(body.ItemId))
            {
                return ApiErrors.Required(nameof(body.ItemId));
            }
            if (body.Status != OfferStatus.Open)
            {
                return ApiErrors.Invalid("Status cannot be changed via update.");
            }
            var communityExists = await dbContext.Communities
                .AnyAsync(community => community.Id == body.CommunityId);
            if (!communityExists)
            {
                return ApiErrors.Invalid("CommunityId does not exist.");
            }
            var offererExists = await dbContext.Users
                .AnyAsync(user => user.Id == body.OffererUserId);
            if (!offererExists)
            {
                return ApiErrors.Invalid("OffererUserId does not exist.");
            }
            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == body.CommunityId
                && membership.UserId == body.OffererUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("OffererUserId is not a member of the community.");
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

            offer.CommunityId = body.CommunityId;
            offer.OffererUserId = body.OffererUserId;
            offer.RequestId = body.RequestId;
            offer.ItemId = body.ItemId;
            offer.Message = body.Message;
            offer.Status = body.Status;

            await dbContext.SaveChangesAsync();
            return Results.Ok(offer);
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
            var offer = await dbContext.Offers.FindAsync(id);
            if (offer is null)
            {
                return ApiErrors.NotFound("Offer");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == offer.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to delete the offer.");
            }
            if (offer.Status != OfferStatus.Open)
            {
                return ApiErrors.Invalid("Offer cannot be deleted unless open.");
            }

            dbContext.Offers.Remove(offer);
            await dbContext.SaveChangesAsync();
            return Results.NoContent();
        });

        group.MapPost("/{id}/accept", async (
            string id,
            AcceptOfferRequest body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }
            var offer = await dbContext.Offers.FindAsync(id);
            if (offer is null)
            {
                return ApiErrors.NotFound("Offer");
            }
            if (offer.Status != OfferStatus.Open)
            {
                return ApiErrors.Invalid("Offer is not open.");
            }
            var item = await dbContext.Items.FindAsync(offer.ItemId);
            if (item is null)
            {
                return ApiErrors.Invalid("Offer item does not exist.");
            }
            if (item.Status != ItemStatus.Available)
            {
                return ApiErrors.Invalid("Item is not available.");
            }

            var actorExists = await dbContext.Users.AnyAsync(user => user.Id == actorUserId);
            if (!actorExists)
            {
                return ApiErrors.Invalid("ActorUserId does not exist.");
            }

            string borrowerUserId;
            Request? request = null;
            if (!string.IsNullOrWhiteSpace(offer.RequestId))
            {
                request = await dbContext.Requests.FindAsync(offer.RequestId);
                if (request is null)
                {
                    return ApiErrors.Invalid("Request does not exist.");
                }
                if (request.Status != RequestStatus.Open)
                {
                    return ApiErrors.Invalid("Request is not open.");
                }
                if (request.RequesterUserId != actorUserId)
                {
                    return ApiErrors.Invalid("ActorUserId must be the requester.");
                }
                borrowerUserId = request.RequesterUserId;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.BorrowerUserId))
                {
                    return ApiErrors.Required(nameof(body.BorrowerUserId));
                }
                if (body.BorrowerUserId != actorUserId)
                {
                    return ApiErrors.Invalid("ActorUserId must match BorrowerUserId.");
                }
                borrowerUserId = body.BorrowerUserId;
            }

            var lenderMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == offer.CommunityId && membership.UserId == offer.OffererUserId);
            if (!lenderMember)
            {
                return ApiErrors.Invalid("OffererUserId is not a member of the community.");
            }
            var borrowerMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == offer.CommunityId && membership.UserId == borrowerUserId);
            if (!borrowerMember)
            {
                return ApiErrors.Invalid("BorrowerUserId is not a member of the community.");
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

            return Results.Ok(loan);
        });

        group.MapPost("/{id}/reject", async (
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
            var offer = await dbContext.Offers.FindAsync(id);
            if (offer is null)
            {
                return ApiErrors.NotFound("Offer");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == offer.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            if (offer.Status != OfferStatus.Open)
            {
                return ApiErrors.Invalid("Offer is not open.");
            }
            var canManage = CanManageCommunity(membership);
            if (!string.IsNullOrWhiteSpace(offer.RequestId))
            {
                var request = await dbContext.Requests.FindAsync(offer.RequestId);
                if (request is null)
                {
                    return ApiErrors.Invalid("Request does not exist.");
                }
                if (!canManage
                    && !string.Equals(request.RequesterUserId, actorUserId, StringComparison.Ordinal))
                {
                    return ApiErrors.Invalid("User is not allowed to reject the offer.");
                }
            }
            else if (!canManage
                && !string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal))
            {
                return ApiErrors.Invalid("User is not allowed to reject the offer.");
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
            return Results.Ok(new { offer.Id, offer.Status });
        });

        group.MapPost("/{id}/withdraw", async (
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
            var offer = await dbContext.Offers.FindAsync(id);
            if (offer is null)
            {
                return ApiErrors.NotFound("Offer");
            }
            var membership = await dbContext.Memberships.FirstOrDefaultAsync(membership =>
                membership.CommunityId == offer.CommunityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (membership is null)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }
            var canManage = CanManageCommunity(membership)
                || string.Equals(offer.OffererUserId, actorUserId, StringComparison.Ordinal);
            if (!canManage)
            {
                return ApiErrors.Invalid("User is not allowed to withdraw the offer.");
            }
            if (offer.Status != OfferStatus.Open)
            {
                return ApiErrors.Invalid("Offer is not open.");
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
            return Results.Ok(new { offer.Id, offer.Status });
        });

        return endpoints;
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

    public sealed record AcceptOfferRequest(string? BorrowerUserId);
}
