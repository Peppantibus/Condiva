namespace Condiva.Api.Features.Storage.Dtos;

public sealed record StorageResolveResponseDto(
    IReadOnlyList<StorageResolveItemDto> Items,
    int ExpiresIn);
