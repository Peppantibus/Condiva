using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Common.Dtos;
using Condiva.Api.Features.Communities.Dtos;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Memberships.Data;
using Condiva.Api.Features.Memberships.Dtos;
using Condiva.Api.Features.Memberships.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
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
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetAllAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRolesByCommunity = await ActorMembershipRoles.GetRolesByCommunityAsync(dbContext, actorUserId);
            var mapped = result.Data!
                .Select(membership =>
                {
                    if (!actorRolesByCommunity.TryGetValue(membership.CommunityId, out var actorRole))
                    {
                        actorRole = MembershipRole.Member;
                    }

                    return mapper.Map<Membership, MembershipListItemDto>(membership) with
                    {
                        AllowedActions = AllowedActionsPolicy.ForMembership(
                            membership,
                            actorUserId,
                            actorRole)
                    };
                })
                .ToList();
            var payload = new PagedResponseDto<MembershipListItemDto>(
                mapped,
                1,
                mapped.Count,
                mapped.Count,
                "joinedAt",
                "desc");
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<MembershipListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/me", async (
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetMineAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRolesByCommunity = await ActorMembershipRoles.GetRolesByCommunityAsync(dbContext, actorUserId);
            var mapped = result.Data!
                .Select(membership =>
                {
                    if (!actorRolesByCommunity.TryGetValue(membership.CommunityId, out var actorRole))
                    {
                        actorRole = MembershipRole.Member;
                    }

                    return mapper.Map<Membership, MembershipListItemDto>(membership) with
                    {
                        AllowedActions = AllowedActionsPolicy.ForMembership(
                            membership,
                            actorUserId,
                            actorRole)
                    };
                })
                .ToList();
            var payload = new PagedResponseDto<MembershipListItemDto>(
                mapped,
                1,
                mapped.Count,
                mapped.Count,
                "joinedAt",
                "desc");
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<MembershipListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/me/communities", async (
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetMyCommunitiesAsync(user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRolesByCommunity = await ActorMembershipRoles.GetRolesByCommunityAsync(dbContext, actorUserId);
            var mapped = result.Data!
                .Select(community =>
                {
                    if (!actorRolesByCommunity.TryGetValue(community.Id, out var actorRole))
                    {
                        actorRole = MembershipRole.Member;
                    }

                    return mapper.Map<Community, CommunityListItemDto>(community) with
                    {
                        AllowedActions = AllowedActionsPolicy.ForCommunity(actorRole)
                    };
                })
                .ToList();
            var payload = new PagedResponseDto<CommunityListItemDto>(
                mapped,
                1,
                mapped.Count,
                mapped.Count,
                "name",
                "asc");
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<CommunityListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/me/communities-context", async (
            ClaimsPrincipal user,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var memberships = await dbContext.Memberships
                .AsNoTracking()
                .Include(membership => membership.Community)
                .Where(membership => membership.UserId == actorUserId)
                .OrderByDescending(membership => membership.Status == MembershipStatus.Active)
                .ThenBy(membership => membership.Community!.Name)
                .ToListAsync();

            var mapped = memberships
                .Select(membership =>
                {
                    var community = membership.Community;
                    var fallbackName = community?.Name ?? string.Empty;
                    var fallbackSlug = community?.Slug ?? string.Empty;
                    var communityAllowedActions = membership.Status == MembershipStatus.Active
                        ? AllowedActionsPolicy.ForCommunity(membership.Role)
                        : ["view"];
                    var membershipAllowedActions = membership.Status == MembershipStatus.Active
                        ? AllowedActionsPolicy.ForMembership(membership, actorUserId, membership.Role)
                        : ["view"];

                    return new MyCommunityContextListItemDto(
                        membership.CommunityId,
                        fallbackName,
                        fallbackSlug,
                        community?.Description,
                        community?.ImageKey,
                        membership.Id,
                        membership.Role.ToString(),
                        membership.Status.ToString(),
                        membership.JoinedAt,
                        communityAllowedActions,
                        membershipAllowedActions);
                })
                .ToList();
            var payload = new PagedResponseDto<MyCommunityContextListItemDto>(
                mapped,
                1,
                mapped.Count,
                mapped.Count,
                "status",
                "desc");

            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<MyCommunityContextListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            IMembershipRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = CurrentUser.GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var result = await repository.GetByIdAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRole = await ActorMembershipRoles.GetRoleAsync(
                dbContext,
                actorUserId,
                result.Data!.CommunityId);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var payload = mapper.Map<Membership, MembershipDetailsDto>(result.Data!) with
            {
                AllowedActions = AllowedActionsPolicy.ForMembership(
                    result.Data!,
                    actorUserId,
                    actorRole.Value)
            };
            return Results.Ok(payload);
        })
            .Produces<MembershipDetailsDto>(StatusCodes.Status200OK);

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
        })
            .Produces<MembershipDetailsDto>(StatusCodes.Status201Created);

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
        })
            .Produces<MembershipDetailsDto>(StatusCodes.Status200OK);

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
        })
            .Produces<MembershipDetailsDto>(StatusCodes.Status200OK);

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
        })
            .Produces(StatusCodes.Status204NoContent);

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
        })
            .Produces(StatusCodes.Status204NoContent);

        return endpoints;
    }

}
