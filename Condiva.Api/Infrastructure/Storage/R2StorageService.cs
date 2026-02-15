using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Condiva.Api.Infrastructure.Storage;

public sealed class R2StorageService : IR2StorageService, IDisposable
{
    private const int MinPresignTtlSeconds = 1;
    private const int MaxPresignTtlSeconds = 604800;

    private readonly CloudFlareR2Options _options;
    private readonly string _secretAccessKey;
    private readonly IAmazonS3 _s3Client;

    public R2StorageService(IOptions<CloudFlareR2Options> optionsAccessor)
    {
        _options = optionsAccessor.Value;
        _secretAccessKey = ResolveSecretAccessKey(_options);
        ValidateOptions(_options, _secretAccessKey);

        DefaultPresignTtlSeconds = Math.Clamp(_options.PresignTtlSeconds, 1, MaxPresignTtlSeconds);
        _s3Client = CreateClient(_options, _secretAccessKey);
    }

    public int DefaultPresignTtlSeconds { get; }

    public string GeneratePresignedPutUrl(string objectKey, string contentType, int expiresInSeconds)
    {
        ValidateObjectKey(objectKey);
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("ContentType is required.", nameof(contentType));
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddSeconds(ClampPresignTtl(expiresInSeconds))
        };

        return _s3Client.GetPreSignedURL(request);
    }

    public string GeneratePresignedGetUrl(string objectKey, int expiresInSeconds)
    {
        ValidateObjectKey(objectKey);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddSeconds(ClampPresignTtl(expiresInSeconds))
        };

        return _s3Client.GetPreSignedURL(request);
    }

    public async Task DeleteObjectAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        ValidateObjectKey(objectKey);

        var request = new DeleteObjectRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey
        };

        await _s3Client.DeleteObjectAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        _s3Client.Dispose();
    }

    private static IAmazonS3 CreateClient(CloudFlareR2Options options, string secretAccessKey)
    {
        return new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKeyId, secretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = "auto"
            });
    }

    private static void ValidateOptions(CloudFlareR2Options options, string secretAccessKey)
    {
        if (string.IsNullOrWhiteSpace(options.AccountId))
        {
            throw new InvalidOperationException("CloudFlareR2:AccountId is not configured.");
        }
        if (string.IsNullOrWhiteSpace(options.AccessKeyId))
        {
            throw new InvalidOperationException("CloudFlareR2:AccessKeyId is not configured.");
        }
        if (string.IsNullOrWhiteSpace(secretAccessKey))
        {
            throw new InvalidOperationException(
                "CloudFlareR2:AccessKeySecret (or CloudFlareR2:SecretAccessKey) is not configured.");
        }
        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            throw new InvalidOperationException("CloudFlareR2:Bucket is not configured.");
        }
        if (string.IsNullOrWhiteSpace(options.Endpoint))
        {
            throw new InvalidOperationException("CloudFlareR2:Endpoint is not configured.");
        }
    }

    private static void ValidateObjectKey(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }
    }

    private static int ClampPresignTtl(int expiresInSeconds)
    {
        return Math.Clamp(expiresInSeconds, MinPresignTtlSeconds, MaxPresignTtlSeconds);
    }

    private static string ResolveSecretAccessKey(CloudFlareR2Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            return options.SecretAccessKey;
        }

        return options.AccessKeySecret;
    }
}
