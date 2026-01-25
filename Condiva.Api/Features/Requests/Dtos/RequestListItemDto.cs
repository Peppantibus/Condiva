using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Requests.Dtos;

public sealed record RequestListItemDto(
    string Id,
    string CommunityId,
    string RequesterUserId,
    string Title,
    string Description,
    string Status,
    DateTime CreatedAt,
    DateTime? NeededFrom,
    DateTime? NeededTo,
    CommunitySummaryDto Community,
    UserSummaryDto Owner);
