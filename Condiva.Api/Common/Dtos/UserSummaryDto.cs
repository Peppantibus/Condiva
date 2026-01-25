namespace Condiva.Api.Common.Dtos;

public sealed record UserSummaryDto(
    string Id,
    string DisplayName,
    string UserName,
    string? AvatarUrl);
