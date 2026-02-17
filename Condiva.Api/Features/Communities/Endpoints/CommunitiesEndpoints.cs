using Condiva.Api.Common.Dtos;
using Condiva.Api.Common.Errors;
using Condiva.Api.Common.Auth;
using Condiva.Api.Common.Auth.Models;
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
using Condiva.Api.Features.Storage.Dtos;
using Condiva.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Condiva.Api.Features.Communities.Endpoints;

public static class CommunitiesEndpoints
{
    private const int DownloadPresignTtlSeconds = 300;
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int MaxIdLength = 64;
    private const int MaxCategoryLength = 64;
    private const int MaxSearchLength = 128;

    public static IEndpointRouteBuilder MapCommunitiesEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/communities");
        group.RequireAuthorization();
        group.WithTags("Communities");

        group.MapGet("/", async (
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
        {
            var actorUserId = GetUserId(user);
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
            var payload = result.Data!
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
            return Results.Ok(payload);
        })
            .Produces<List<CommunityListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (
            string id,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
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

            var result = await repository.GetByIdAsync(normalizedId!, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRole = await ActorMembershipRoles.GetRoleAsync(
                dbContext,
                actorUserId,
                normalizedId!);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var payload = mapper.Map<Community, CommunityDetailsDto>(result.Data!) with
            {
                AllowedActions = AllowedActionsPolicy.ForCommunity(actorRole.Value)
            };
            return Results.Ok(payload);
        })
            .Produces<CommunityDetailsDto>(StatusCodes.Status200OK);

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
        })
            .Produces<InviteCodeResponseDto>(StatusCodes.Status200OK);

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
        })
            .Produces<InviteCodeResponseDto>(StatusCodes.Status200OK);

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
        })
            .Produces<MembershipDetailsDto>(StatusCodes.Status201Created);

        group.MapGet("/{id}/members", async (
            string id,
            string? search,
            string? role,
            string? status,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
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
            var communityId = normalizedId!;

            var normalizedSearch = Normalize(search);
            var searchError = ValidateSearch(normalizedSearch);
            if (searchError is not null)
            {
                return searchError;
            }

            var normalizedRole = Normalize(role);
            MembershipRole? roleFilter = null;
            if (!string.IsNullOrWhiteSpace(normalizedRole))
            {
                if (!Enum.TryParse<MembershipRole>(normalizedRole, true, out var parsedRole))
                {
                    return ApiErrors.Invalid("Invalid role filter.");
                }

                roleFilter = parsedRole;
            }

            var normalizedStatus = Normalize(status);
            MembershipStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(normalizedStatus))
            {
                if (!Enum.TryParse<MembershipStatus>(normalizedStatus, true, out var parsedStatus))
                {
                    return ApiErrors.Invalid("Invalid status filter.");
                }

                statusFilter = parsedStatus;
            }

            var actorMembership = await dbContext.Memberships.AsNoTracking().FirstOrDefaultAsync(membership =>
                membership.CommunityId == communityId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (actorMembership is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var pageNumber = ClampPage(page);
            var size = ClampPageSize(pageSize);

            var query = dbContext.Memberships
                .AsNoTracking()
                .Include(membership => membership.User)
                .Where(membership => membership.CommunityId == communityId);

            if (roleFilter.HasValue)
            {
                query = query.Where(membership => membership.Role == roleFilter.Value);
            }

            if (statusFilter.HasValue)
            {
                query = query.Where(membership => membership.Status == statusFilter.Value);
            }

            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                query = query.Where(membership =>
                    membership.UserId.Contains(normalizedSearch)
                    || (membership.User != null
                        && (
                            (membership.User.Username ?? string.Empty).Contains(normalizedSearch)
                            || (membership.User.Name ?? string.Empty).Contains(normalizedSearch)
                            || (membership.User.LastName ?? string.Empty).Contains(normalizedSearch)
                            || (((membership.User.Name ?? string.Empty) + " " + (membership.User.LastName ?? string.Empty))
                                .Contains(normalizedSearch)))));
            }

            var total = await query.CountAsync();
            var members = await query
                .OrderByDescending(membership => membership.JoinedAt ?? membership.CreatedAt)
                .ThenBy(membership => membership.Id)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .ToListAsync();

            var memberUserIds = members
                .Select(membership => membership.UserId)
                .Distinct()
                .ToArray();
            var reputationsByUserId = await dbContext.Reputations
                .AsNoTracking()
                .Where(reputation =>
                    reputation.CommunityId == communityId
                    && memberUserIds.Contains(reputation.UserId))
                .ToDictionaryAsync(reputation => reputation.UserId);

            var payload = members
                .Select(membership =>
                {
                    reputationsByUserId.TryGetValue(membership.UserId, out var reputation);
                    var reputationSummary = new CommunityMemberReputationSummaryDto(
                        reputation?.Score ?? 0,
                        reputation?.LendCount ?? 0,
                        reputation?.ReturnCount ?? 0,
                        reputation?.OnTimeReturnCount ?? 0);

                    return new CommunityMemberListItemDto(
                        membership.Id,
                        membership.UserId,
                        membership.CommunityId,
                        membership.Role.ToString(),
                        membership.Status.ToString(),
                        membership.JoinedAt,
                        BuildUserSummary(membership.User, membership.UserId, storageService),
                        reputationSummary,
                        AllowedActionsPolicy.ForMembership(membership, actorUserId, actorMembership.Role));
                })
                .ToList();

            return Results.Ok(new PagedResponseDto<CommunityMemberListItemDto>(
                payload,
                pageNumber,
                size,
                total));
        })
            .Produces<PagedResponseDto<CommunityMemberListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}/requests/feed", async (
            string id,
            string? status,
            bool? excludingMine,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
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
                excludingMine,
                pageNumber,
                size,
                user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var actorRole = await ActorMembershipRoles.GetRoleAsync(dbContext, actorUserId, normalizedId!);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var mapped = result.Data!.Items
                .Select(request => mapper.Map<Request, RequestListItemDto>(request) with
                {
                    AllowedActions = AllowedActionsPolicy.ForRequest(request, actorUserId, actorRole.Value)
                })
                .ToList();
            var payload = new PagedResponseDto<RequestListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total);
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<RequestListItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id}/items/available", async (
            string id,
            string? category,
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper,
            CondivaDbContext dbContext) =>
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

            var actorRole = await ActorMembershipRoles.GetRoleAsync(dbContext, actorUserId, normalizedId!);
            if (actorRole is null)
            {
                return ApiErrors.Forbidden("User is not a member of the community.");
            }

            var mapped = result.Data!.Items
                .Select(item => mapper.Map<Item, ItemListItemDto>(item) with
                {
                    AllowedActions = AllowedActionsPolicy.ForItem(item, actorUserId, actorRole.Value)
                })
                .ToList();
            var payload = new PagedResponseDto<ItemListItemDto>(
                mapped,
                result.Data.Page,
                result.Data.PageSize,
                result.Data.Total);
            return Results.Ok(payload);
        })
            .Produces<PagedResponseDto<ItemListItemDto>>(StatusCodes.Status200OK);

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
        })
            .Produces<CommunityDetailsDto>(StatusCodes.Status201Created);

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
        })
            .Produces<CommunityDetailsDto>(StatusCodes.Status200OK);

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
        })
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{id}/image/presign", async (
            string id,
            StoragePresignRequestDto body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
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

            var community = await dbContext.Communities.FirstOrDefaultAsync(
                foundCommunity => foundCommunity.Id == normalizedId);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }

            var canManage = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == normalizedId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active
                && membership.Role == MembershipRole.Owner);
            if (!canManage)
            {
                return ApiErrors.Forbidden("User is not allowed to manage the community image.");
            }

            if (string.IsNullOrWhiteSpace(body.FileName))
            {
                return ApiErrors.Required(nameof(body.FileName));
            }

            if (!StorageImageKeyHelper.IsAllowedImageContentType(body.ContentType))
            {
                return ApiErrors.Invalid("Unsupported contentType.");
            }

            var scope = $"communities/{normalizedId}/image";
            var objectKey = StorageImageKeyHelper.BuildScopedObjectKey(
                scope,
                StorageImageKeyHelper.SanitizeFileName(body.FileName));
            var uploadUrl = storageService.GeneratePresignedPutUrl(
                objectKey,
                body.ContentType.Trim(),
                storageService.DefaultPresignTtlSeconds);

            return Results.Ok(new StoragePresignResponseDto(
                objectKey,
                uploadUrl,
                storageService.DefaultPresignTtlSeconds));
        })
            .Produces<StoragePresignResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id}/image/confirm", async (
            string id,
            StorageConfirmRequestDto body,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
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

            if (!StorageImageKeyHelper.TryNormalizeObjectKey(body.ObjectKey, out var objectKey))
            {
                return ApiErrors.Invalid("Invalid objectKey.");
            }

            var scope = $"communities/{normalizedId}/image";
            if (!StorageImageKeyHelper.IsScopedKey(objectKey, scope))
            {
                return ApiErrors.Invalid("objectKey is outside allowed scope.");
            }

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(
                foundMembership =>
                    foundMembership.CommunityId == normalizedId
                    && foundMembership.UserId == actorUserId
                    && foundMembership.Status == MembershipStatus.Active,
                cancellationToken);
            if (membership is null || membership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Forbidden("User is not allowed to manage the community image.");
            }

            var community = await dbContext.Communities.FirstOrDefaultAsync(
                foundCommunity => foundCommunity.Id == normalizedId,
                cancellationToken);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }

            var previousObjectKey = community.ImageKey;
            community.ImageKey = objectKey;
            await dbContext.SaveChangesAsync(cancellationToken);

            await TryDeletePreviousObjectAsync(
                previousObjectKey,
                objectKey,
                storageService,
                loggerFactory.CreateLogger("Communities.Image"),
                cancellationToken);

            return Results.Ok(new { objectKey });
        })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/image", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService) =>
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

            var community = await dbContext.Communities.FirstOrDefaultAsync(
                foundCommunity => foundCommunity.Id == normalizedId);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }

            var isMember = await dbContext.Memberships.AnyAsync(membership =>
                membership.CommunityId == normalizedId
                && membership.UserId == actorUserId
                && membership.Status == MembershipStatus.Active);
            if (!isMember)
            {
                return ApiErrors.Invalid("User is not a member of the community.");
            }

            if (string.IsNullOrWhiteSpace(community.ImageKey))
            {
                return ApiErrors.NotFound("CommunityImage");
            }

            var downloadUrl = storageService.GeneratePresignedGetUrl(community.ImageKey, DownloadPresignTtlSeconds);
            return Results.Ok(new CommunityImageResponseDto(community.ImageKey, downloadUrl));
        })
            .Produces<CommunityImageResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}/image", async (
            string id,
            ClaimsPrincipal user,
            CondivaDbContext dbContext,
            IR2StorageService storageService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
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

            var membership = await dbContext.Memberships.FirstOrDefaultAsync(
                foundMembership =>
                    foundMembership.CommunityId == normalizedId
                    && foundMembership.UserId == actorUserId
                    && foundMembership.Status == MembershipStatus.Active,
                cancellationToken);
            if (membership is null || membership.Role != MembershipRole.Owner)
            {
                return ApiErrors.Forbidden("User is not allowed to manage the community image.");
            }

            var community = await dbContext.Communities.FirstOrDefaultAsync(
                foundCommunity => foundCommunity.Id == normalizedId,
                cancellationToken);
            if (community is null)
            {
                return ApiErrors.NotFound("Community");
            }

            var previousObjectKey = community.ImageKey;
            if (string.IsNullOrWhiteSpace(previousObjectKey))
            {
                return Results.NoContent();
            }

            community.ImageKey = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            var logger = loggerFactory.CreateLogger("Communities.Image");
            try
            {
                await storageService.DeleteObjectAsync(previousObjectKey, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed deleting community image from R2. key: {ObjectKey}", previousObjectKey);
            }

            return Results.NoContent();
        })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id}/invite-link", async (
            string id,
            ClaimsPrincipal user,
            ICommunityRepository repository,
            IMapper mapper,
            IConfiguration config,
            HttpContext http) =>
        {
            // Reuse invite-code permission logic.
            var result = await repository.GetInviteCodeAsync(id, user);
            if (!result.IsSuccess)
            {
                return result.Error!;
            }

            var info = result.Data!;

            var frontendBase = config.GetValue<string>("Frontend:BaseUrl");
            if (string.IsNullOrWhiteSpace(frontendBase))
            {
                frontendBase = $"{http.Request.Scheme}://{http.Request.Host}";
            }

            var url = $"{frontendBase.TrimEnd('/')}/join?code={Uri.EscapeDataString(info.EnterCode)}";

            return Results.Ok(new InviteLinkResponseDto(url, info.ExpiresAt));
        })
            .Produces<InviteLinkResponseDto>(StatusCodes.Status200OK);

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

    private static IResult? ValidateSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        return search.Length > MaxSearchLength
            ? ApiErrors.Invalid("Search filter is too long.")
            : null;
    }

    private static UserSummaryDto BuildUserSummary(
        User? user,
        string fallbackUserId,
        IR2StorageService storageService)
    {
        if (user is null)
        {
            return new UserSummaryDto(fallbackUserId, string.Empty, string.Empty, null);
        }

        var displayName = string.Empty;
        if (!string.IsNullOrWhiteSpace(user.Name) || !string.IsNullOrWhiteSpace(user.LastName))
        {
            displayName = $"{user.Name} {user.LastName}".Trim();
        }
        else if (!string.IsNullOrWhiteSpace(user.Username))
        {
            displayName = user.Username;
        }
        else
        {
            displayName = user.Id;
        }

        var avatarUrl = string.IsNullOrWhiteSpace(user.ProfileImageKey)
            ? null
            : storageService.GeneratePresignedGetUrl(user.ProfileImageKey, DownloadPresignTtlSeconds);

        return new UserSummaryDto(user.Id, displayName, user.Username ?? string.Empty, avatarUrl);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static async Task TryDeletePreviousObjectAsync(
        string? previousObjectKey,
        string currentObjectKey,
        IR2StorageService storageService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(previousObjectKey)
            || string.Equals(previousObjectKey, currentObjectKey, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await storageService.DeleteObjectAsync(previousObjectKey, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed deleting previous community image from R2. key: {ObjectKey}", previousObjectKey);
        }
    }

    public sealed record InviteLinkResponseDto(string Url, DateTime ExpiresAt);
    public sealed record CommunityImageResponseDto(string ObjectKey, string DownloadUrl);
}
