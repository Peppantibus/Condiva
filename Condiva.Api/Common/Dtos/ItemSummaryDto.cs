namespace Condiva.Api.Common.Dtos;

public sealed record ItemSummaryDto(
    string Id,
    string Name,
    string Description,
    string? Category,
    string Status,
    string OwnerUserId);
