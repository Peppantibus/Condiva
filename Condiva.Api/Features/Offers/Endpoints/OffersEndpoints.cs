using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Loans.Dtos;
using Condiva.Api.Features.Loans.Models;
using Condiva.Api.Features.Offers.Data;
using Condiva.Api.Features.Offers.Dtos;
using Condiva.Api.Features.Offers.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

        group.MapGet("/", async (
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetAllAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Offer, OfferListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        })
            .Produces<List<OfferListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Offer, OfferDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<OfferDetailsDto>(StatusCodes.Status200OK);

        group.MapGet("/me", async (
            string? communityId,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetMineAsync(communityId, status, page, pageSize, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var mapped = mapper.MapList<Offer, OfferListItemDto>(result.Data!.Items).ToList();
            var payload = new PagedResponseDto<OfferListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total);
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<OfferListItemDto>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateOfferRequestDto body,
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<OfferStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Offer
            {
                CommunityId = body.CommunityId,
                OffererUserId = body.OffererUserId,
                RequestId = body.RequestId,
                ItemId = body.ItemId,
                Message = body.Message,
                Status = statusValue
            };

            var result = await repository.CreateAsync(model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Offer, OfferDetailsDto>(result.Data!);
            return Results.Created($"/api/offers/{payload.Id}", payload);
        })
            .Produces<OfferDetailsDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", async (
            string id,
            UpdateOfferRequestDto body,
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<OfferStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Offer
            {
                CommunityId = body.CommunityId,
                OffererUserId = body.OffererUserId,
                RequestId = body.RequestId,
                ItemId = body.ItemId,
                Message = body.Message,
                Status = statusValue
            };

            var result = await repository.UpdateAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Offer, OfferDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<OfferDetailsDto>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IOfferRepository repository) =>
        {
            var result = await repository.DeleteAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{id}/accept", async (
            string id,
            AcceptOfferRequestDto body,
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            var model = new AcceptOfferRequest(body.BorrowerUserId);

            var result = await repository.AcceptAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Created($"/api/loans/{payload.Id}", payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status201Created);

        group.MapPost("/{id}/reject", async (
            string id,
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.RejectAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Offer, OfferStatusResponseDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<OfferStatusResponseDto>(StatusCodes.Status200OK);

        group.MapPost("/{id}/withdraw", async (
            string id,
            ClaimsPrincipal user,
            IOfferRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.WithdrawAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Offer, OfferStatusResponseDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<OfferStatusResponseDto>(StatusCodes.Status200OK);

        return endpoints;
    }
}
