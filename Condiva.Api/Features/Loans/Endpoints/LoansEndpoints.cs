using Condiva.Api.Common.Errors;
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
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetAllAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Loan, LoanListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        });

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
        });

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
        });

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
        });

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
        });

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
        });

        group.MapPost("/{id}/return", async (
            string id,
            ClaimsPrincipal user,
            ILoanRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.ReturnAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Loan, LoanDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        return endpoints;
    }
}
