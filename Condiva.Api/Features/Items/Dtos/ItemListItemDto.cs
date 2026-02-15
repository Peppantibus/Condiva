using Condiva.Api.Common.Dtos;

namespace Condiva.Api.Features.Items.Dtos;

public sealed record ItemListItemDto(
    string Id,
    string CommunityId,
    string OwnerUserId,
    string Name,
    string Description,
    string? Category,
    string? ImageKey,
    string Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    UserSummaryDto Owner);
