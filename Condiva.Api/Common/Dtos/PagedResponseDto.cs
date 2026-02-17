namespace Condiva.Api.Common.Dtos;

public sealed record PagedResponseDto<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int Total,
    string? Sort = null,
    string? Order = null);
