namespace Condiva.Api.Common.Results;

public sealed record CursorPagedResult<T>(
    IReadOnlyList<T> Items,
    int PageSize,
    int Total,
    string? Cursor,
    string? NextCursor);
