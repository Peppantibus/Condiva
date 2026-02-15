namespace Condiva.Api.Infrastructure.Storage;

public interface IR2StorageService
{
    int DefaultPresignTtlSeconds { get; }

    string GeneratePresignedPutUrl(string objectKey, string contentType, int expiresInSeconds);

    string GeneratePresignedGetUrl(string objectKey, int expiresInSeconds);

    Task DeleteObjectAsync(string objectKey, CancellationToken cancellationToken = default);
}
