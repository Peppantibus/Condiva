using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Communities.Dtos;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Memberships.Data;
using Condiva.Api.Features.Memberships.Dtos;
using Condiva.Api.Features.Memberships.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Condiva.Api.Features.Memberships.Endpoints;

public static class MembershipsEndpoints
{
    public static IEndpointRouteBuilder MapMembershipsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/memberships");
        group.RequireAuthorization();
        group.WithTags("Memberships");

        group.MapGet("/", async (
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetAllAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Membership, MembershipListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        });

        group.MapGet("/me/communities", async (
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetMyCommunitiesAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.MapList<Community, CommunityListItemDto>(result.Data!)
                .ToList();
            return Results.Ok(payload);
        });

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Membership, MembershipDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapPost("/", async (
            CreateMembershipRequestDto body,
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper) =>
        {
            var model = new Membership
            {
                CommunityId = body.CommunityId
            };

            var result = await repository.CreateAsync(model, body.EnterCode, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Membership, MembershipDetailsDto>(result.Data!);
            return Results.Created($"/api/memberships/{payload.Id}", payload);
        });

        group.MapPut("/{id}", async (
            string id,
            UpdateMembershipRequestDto body,
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper) =>
        {
            if (string.IsNullOrWhiteSpace(body.Role))
            {
                return ApiErrors.Required(nameof(body.Role));
            }
            if (string.IsNullOrWhiteSpace(body.Status))
            {
                return ApiErrors.Required(nameof(body.Status));
            }
            if (!Enum.TryParse<MembershipRole>(body.Role, true, out var roleValue))
            {
                return ApiErrors.Invalid("Invalid role.");
            }
            if (!Enum.TryParse<MembershipStatus>(body.Status, true, out var statusValue))
            {
                return ApiErrors.Invalid("Invalid status.");
            }

            var model = new Membership
            {
                UserId = body.UserId,
                CommunityId = body.CommunityId,
                Role = roleValue,
                Status = statusValue,
                InvitedByUserId = body.InvitedByUserId,
                JoinedAt = body.JoinedAt
            };

            var result = await repository.UpdateAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Membership, MembershipDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapPost("/{id}/role", async (
            string id,
            UpdateMembershipRoleRequestDto body,
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper) =>
        {
            var model = new UpdateMembershipRoleRequest(body.Role);

            var result = await repository.UpdateRoleAsync(id, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Membership, MembershipDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IMembershipRepository repository) =>
        {
            var result = await repository.DeleteAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        });

        group.MapPost("/leave/{communityId}", async (
            string communityId,
            ClaimsPrincipal user,
            IMembershipRepository repository) =>
        {
            var result = await repository.LeaveAsync(communityId, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        });

        return endpoints;
    }

}
