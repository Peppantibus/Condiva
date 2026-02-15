namespace Condiva.Api.Infrastructure.Storage;

public class CloudFlareR2Options
{
    public string AccountId { get; set; } = default!;
    public string AccessKeyId { get; set; } = default!;
    public string SecretAccessKey { get; set; } = string.Empty;
    public string AccessKeySecret { get; set; } = string.Empty;
    public string Bucket { get; set; } = default!;
    public string Endpoint { get; set; } = default!;
    public int PresignTtlSeconds { get; set; }
}
