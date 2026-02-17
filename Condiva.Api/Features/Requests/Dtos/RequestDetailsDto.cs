using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Requests.Dtos;

public sealed record RequestDetailsDto(
    string Id,
    string CommunityId,
    string RequesterUserId,
    string Title,
    string Description,
    string? ImageKey,
    string Status,
    DateTime CreatedAt,
    DateTime? NeededFrom,
    DateTime? NeededTo,
    CommunitySummaryDto Community,
    UserSummaryDto Owner,
    string[]? AllowedActions = null);
