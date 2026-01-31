using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Items.Data;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Items.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Items.Endpoints;

public static class ItemsEndpoints
{
    public static IEndpointRouteBuilder MapItemsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/items");
        group.RequireAuthorization();
        group.WithTags("Items");

        group.MapGet("/", async (
            string? communityId,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(communityId))
            {
                return ApiErrors.Required(nameof(communityId));
            }

            var result = await repository.GetAllAsync(communityId, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Item, ItemListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        })
            .Produces<List<ItemListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            CreateItemRequestDto body,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<ItemStatus>(body.Status, true, out var status))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Item
            {
                CommunityId = body.CommunityId,
                OwnerUserId = body.OwnerUserId,
                Name = body.Name,
                Description = body.Description,
                Category = body.Category,
                Status = status
            };

            var result = await repository.CreateAsync(model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Created($"/api/items/{payload.Id}", payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status201Created);

        group.MapPut("/{id}", async (
            string id,
            UpdateItemRequestDto body,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<ItemStatus>(body.Status, true, out var status))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Item
            {
                CommunityId = body.CommunityId,
                OwnerUserId = body.OwnerUserId,
                Name = body.Name,
                Description = body.Description,
                Category = body.Category,
                Status = status
            };

            var result = await repository.UpdateAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Ok(payload);
        })
            .Produces<ItemDetailsDto>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IItemRepository repository) =>
        {
            var result = await repository.DeleteAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }
}
