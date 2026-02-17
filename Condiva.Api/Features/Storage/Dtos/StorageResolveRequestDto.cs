namespace Condiva.Api.Features.Storage.Dtos;

public sealed class StorageResolveRequestDto
{
    public IReadOnlyList<string> ObjectKeys { get; init; } = Array.Empty<string>();
}
