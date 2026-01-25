namespace Condiva.Api.Features.Items.Dtos;

public sealed record CreateItemRequestDto(
    string CommunityId,
    string OwnerUserId,
    string Name,
    string Description,
    string? Category,
    string Status);
