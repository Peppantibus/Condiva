using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Offers.Dtos;
using Condiva.Api.Features.Offers.Models;
using Condiva.Api.Features.Requests.Data;
using Condiva.Api.Features.Requests.Dtos;
using Condiva.Api.Features.Requests.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Requests.Endpoints;

public static class RequestsEndpoints
{
    public static IEndpointRouteBuilder MapRequestsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/requests");
        group.RequireAuthorization();
        group.WithTags("Requests");

        group.MapGet("/", async (
            string? communityId,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(communityId))
            {
                return ApiErrors.Required(nameof(communityId));
            }

            var result = await repository.GetAllAsync(communityId, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Request, RequestListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        });

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetByIdAsync(id, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Request, RequestDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapGet("/{id}/offers", async (
            string id,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetOffersAsync(id, page, pageSize, user, dbContext);
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
        });

        group.MapGet("/me", async (
            string? communityId,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetMineAsync(communityId, status, page, pageSize, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var mapped = mapper.MapList<Request, RequestListItemDto>(result.Data!.Items).ToList();
            var payload = new PagedResponseDto<RequestListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total);
            return Results.Ok(payload);
        });

        group.MapPost("/", async (
            CreateRequestRequestDto body,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<RequestStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Request
            {
                CommunityId = body.CommunityId,
                RequesterUserId = body.RequesterUserId,
                Title = body.Title,
                Description = body.Description,
                Status = statusValue,
                NeededFrom = body.NeededFrom,
                NeededTo = body.NeededTo
            };

            var result = await repository.CreateAsync(model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Request, RequestDetailsDto>(result.Data!);
            return Results.Created($"/api/requests/{payload.Id}", payload);
        });

        group.MapPut("/{id}", async (
            string id,
            UpdateRequestRequestDto body,
            ClaimsPrincipal user,
            IRequestRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<RequestStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Request
            {
                CommunityId = body.CommunityId,
                RequesterUserId = body.RequesterUserId,
                Title = body.Title,
                Description = body.Description,
                Status = statusValue,
                NeededFrom = body.NeededFrom,
                NeededTo = body.NeededTo
            };

            var result = await repository.UpdateAsync(id, model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Request, RequestDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IRequestRepository repository,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.DeleteAsync(id, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        });

        return endpoints;
    }
}
