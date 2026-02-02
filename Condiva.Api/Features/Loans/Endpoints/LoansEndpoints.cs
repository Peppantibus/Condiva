using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Loans.Data;
using Condiva.Api.Features.Loans.Dtos;
using Condiva.Api.Features.Loans.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
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

        group.MapGet("/", async (
            string? communityId,
            string? status,
            DateTime? from,
            DateTime? to,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetAllAsync(
                communityId,
                status,
                from,
                to,
                page,
                pageSize,
                user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var mapped = mapper.MapList<Loan, LoanListItemDto>(result.Data!.Items)
                .ToList();
            var usePaging = page.HasValue || pageSize.HasValue;
            if (!usePaging)
            {
                return Results.Ok(mapped);
            }

            var payload = new PagedResponseDto<LoanListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total);
            return Results.Ok(payload);
        })
            .Produces<List<LoanListItemDto>>(StatusCodes.Status200OK)
            .Produces<PagedResponseDto<LoanListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateLoanRequestDto body,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<LoanStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Loan
            {
                CommunityId = body.CommunityId,
                ItemId = body.ItemId,
                LenderUserId = body.LenderUserId,
                BorrowerUserId = body.BorrowerUserId,
                RequestId = body.RequestId,
                OfferId = body.OfferId,
                Status = statusValue,
                StartAt = body.StartAt ?? default,
                DueAt = body.DueAt
            };

            var result = await repository.CreateAsync(model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Created($"/api/loans/{payload.Id}", payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", async (
            string id,
            UpdateLoanRequestDto body,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<LoanStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Loan
            {
                CommunityId = body.CommunityId,
                ItemId = body.ItemId,
                LenderUserId = body.LenderUserId,
                BorrowerUserId = body.BorrowerUserId,
                RequestId = body.RequestId,
                OfferId = body.OfferId,
                Status = statusValue,
                StartAt = body.StartAt ?? default,
                DueAt = body.DueAt,
                ReturnedAt = body.ReturnedAt
            };

            var result = await repository.UpdateAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            ILoanRepository repository) =>
        {
            var result = await repository.DeleteAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{id}/start", async (
            string id,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.StartAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/{id}/return-request", async (
            string id,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.ReturnRequestAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/{id}/return-confirm", async (
            string id,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.ReturnConfirmAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/{id}/return-cancel", async (
            string id,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.ReturnCancelAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<LoanDetailsDto>(StatusCodes.Status200OK);

        return endpoints;
    }
}
