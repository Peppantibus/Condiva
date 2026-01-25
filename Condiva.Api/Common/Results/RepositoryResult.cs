namespace Condiva.Api.Common.Results;

public readonly record struct RepositoryResult<T>(T? Data, IResult? Error)
{
    public bool IsSuccess => Error is null;

    public static RepositoryResult<T> Success(T data) => new(data, null);

    public static RepositoryResult<T> Failure(IResult error) => new(default, error);
}
