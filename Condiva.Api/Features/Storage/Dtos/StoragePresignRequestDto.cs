namespace Condiva.Api.Features.Storage.Dtos;

public sealed class StoragePresignRequestDto
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
}
