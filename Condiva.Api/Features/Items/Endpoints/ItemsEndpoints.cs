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

            var payload = mapper.MapList<Item, ItemListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        });

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var result = await repository.GetByIdAsync(id, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapPost("/", async (
            CreateItemRequestDto body,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
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

            var result = await repository.CreateAsync(model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Created($"/api/items/{payload.Id}", payload);
        });

        group.MapPut("/{id}", async (
            string id,
            UpdateItemRequestDto body,
            ClaimsPrincipal user,
            IItemRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
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

            var result = await repository.UpdateAsync(id, model, user, dbContext);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Item, ItemDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IItemRepository repository,
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
