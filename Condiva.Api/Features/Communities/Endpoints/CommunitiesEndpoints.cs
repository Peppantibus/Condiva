using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Mapping;
using Condiva.Api.Features.Communities.Data;
using Condiva.Api.Features.Communities.Dtos;
using Condiva.Api.Features.Communities.Models;
using Condiva.Api.Features.Items.Dtos;
using Condiva.Api.Features.Items.Models;
using Condiva.Api.Features.Memberships.Dtos;
using Condiva.Api.Features.Memberships.Models;
using Condiva.Api.Features.Requests.Dtos;
using Condiva.Api.Features.Requests.Models;
using System.Security.Claims;

namespace Condiva.Api.Features.Communities.Endpoints;

public static class CommunitiesEndpoints
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxIdLength = 64;
    private const int MaxCategoryLength = 64;

    public static IEndpointRouteBuilder MapCommunitiesEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/communities");
        group.RequireAuthorization();
        group.WithTags("Communities");

        group.MapGet("/", async (
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var result = await repository.GetAllAsync(user);
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
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var normalizedId = Normalize(id);
            var idError = ValidateId(normalizedId);
            if (idError is not null)
            {
                return idError;
            }

            var result = await repository.GetByIdAsync(normalizedId!, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Community, CommunityDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapGet("/{id}/invite-code", async (
            string id,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var normalizedId = Normalize(id);
            var idError = ValidateId(normalizedId);
            if (idError is not null)
            {
                return idError;
            }

            var result = await repository.GetInviteCodeAsync(normalizedId!, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<InviteCodeInfo, InviteCodeResponseDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapPost("/{id}/invite-code/rotate", async (
            string id,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var normalizedId = Normalize(id);
            var idError = ValidateId(normalizedId);
            if (idError is not null)
            {
                return idError;
            }

            var result = await repository.RotateInviteCodeAsync(normalizedId!, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<InviteCodeInfo, InviteCodeResponseDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapPost("/join", async (
            JoinCommunityRequestDto body,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var inviteCode = Normalize(body.EnterCode);
            if (string.IsNullOrWhiteSpace(inviteCode))
            {
                return ApiErrors.Required(nameof(body.EnterCode));
            }

            var result = await repository.JoinAsync(inviteCode, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Membership, MembershipDetailsDto>(result.Data!);
            return Results.Created($"/api/memberships/{payload.Id}", payload);
        });

        group.MapGet("/{id}/requests/feed", async (
            string id,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var normalizedId = Normalize(id);
            var idError = ValidateId(normalizedId);
            if (idError is not null)
            {
                return idError;
            }

            var normalizedStatus = Normalize(status);
            var statusError = ValidateStatus(normalizedStatus);
            if (statusError is not null)
            {
                return statusError;
            }

            var pageNumber = ClampPage(page);
            var size = ClampPageSize(pageSize);

            var result = await repository.GetRequestsFeedAsync(
                normalizedId!,
                normalizedStatus,
                pageNumber,
                size,
                user);
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

        group.MapGet("/{id}/items/available", async (
            string id,
            string? category,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var normalizedId = Normalize(id);
            var idError = ValidateId(normalizedId);
            if (idError is not null)
            {
                return idError;
            }

            var normalizedCategory = Normalize(category);
            var categoryError = ValidateCategory(normalizedCategory);
            if (categoryError is not null)
            {
                return categoryError;
            }

            var pageNumber = ClampPage(page);
            var size = ClampPageSize(pageSize);

            var result = await repository.GetAvailableItemsAsync(
                normalizedId!,
                normalizedCategory,
                pageNumber,
                size,
                user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var mapped = mapper.MapList<Item, ItemListItemDto>(result.Data!.Items).ToList();
            var payload = new PagedResponseDto<ItemListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total);
            return Results.Ok(payload);
        });

        group.MapPost("/", async (
            CreateCommunityRequestDto body,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var model = new Community
            {
                Name = body.Name,
                Slug = body.Slug,
                Description = body.Description,
                CreatedByUserId = actorUserId
            };

            var result = await repository.CreateAsync(model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Community, CommunityDetailsDto>(result.Data!);
            return Results.Created($"/api/communities/{payload.Id}", payload);
        });

        group.MapPut("/{id}", async (
            string id,
            UpdateCommunityRequestDto body,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper) =>
        {
            var actorUserId = GetUserId(user);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return ApiErrors.Unauthorized();
            }

            var normalizedId = Normalize(id);
            var idError = ValidateId(normalizedId);
            if (idError is not null)
            {
                return idError;
            }

            var model = new Community
            {
                Name = body.Name,
                Slug = body.Slug,
                Description = body.Description
            };

            var result = await repository.UpdateAsync(normalizedId!, model, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var payload = mapper.Map<Community, CommunityDetailsDto>(result.Data!);
            return Results.Ok(payload);
        });

        group.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal user,
            ICommunityRepository repository) =>
        {
            var normalizedId = Normalize(id);
            var idError = ValidateId(normalizedId);
            if (idError is not null)
            {
                return idError;
            }

            var result = await repository.DeleteAsync(normalizedId!, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            return Results.NoContent();
        });

        group.MapGet("/{id}/invite-link", async (
            string id,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper,
            IConfiguration config,
            HttpContext http) =>
        {
            // Riusa la logica di permessi/recupero code già esistente
            var result = await repository.GetInviteCodeAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var info = result.Data!; // InviteCodeInfo(code, expiresAt)

            // Base URL del frontend (consigliato metterlo in config)
            var frontendBase = config.GetValue<string>("Frontend:BaseUrl");
            if (string.IsNullOrWhiteSpace(frontendBase))
            {
                // fallback: usa host corrente (solo se ha senso per il tuo deployment)
                frontendBase = $"{http.Request.Scheme}://{http.Request.Host}";
            }

            // pagina join lato frontend
            var url = $"{frontendBase.TrimEnd('/')}/join?code={Uri.EscapeDataString(info.EnterCode)}";

            return Results.Ok(new InviteLinkResponseDto(url, info.ExpiresAt));
        });

        return endpoints;
    }

    private static string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;
    }

    private static int ClampPage(int? page)
    {
        var value = page.GetValueOrDefault(DefaultPage);
        return value < 1 ? 1 : value;
    }

    private static int ClampPageSize(int? pageSize)
    {
        var value = pageSize.GetValueOrDefault(DefaultPageSize);
        if (value < 1)
        {
            return 1;
        }

        return value > MaxPageSize ? MaxPageSize : value;
    }

    private static IResult? ValidateId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return ApiErrors.Required("id");
        }

        if (id.Length > MaxIdLength)
        {
            return ApiErrors.Invalid("Invalid id.");
        }

        return null;
    }

    private static IResult? ValidateStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return Enum.TryParse<RequestStatus>(status, true, out _)
            ? null
            : ApiErrors.Invalid("Invalid status filter.");
    }

    private static IResult? ValidateCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        return category.Length > MaxCategoryLength
            ? ApiErrors.Invalid("Category is too long.")
            : null;
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public sealed record InviteLinkResponseDto(string Url, DateTime ExpiresAt);
}
