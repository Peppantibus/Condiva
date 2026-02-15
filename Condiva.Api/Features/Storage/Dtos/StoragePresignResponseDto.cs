namespace Condiva.Api.Features.Storage.Dtos;

public sealed record StoragePresignResponseDto(
    string ObjectKey,
    string UploadUrl,
    int ExpiresIn);
