namespace Condiva.Api.Features.Storage.Dtos;

public sealed record StorageResolveItemDto(
    string ObjectKey,
    string DownloadUrl);
