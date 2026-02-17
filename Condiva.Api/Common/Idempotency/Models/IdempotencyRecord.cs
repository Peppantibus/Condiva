namespace Condiva.Api.Common.Idempotency.Models;

public sealed class IdempotencyRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string ActorUserId { get; set; }
    public required string Method { get; set; }
    public required string Path { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ResponseContentType { get; set; }
    public string? ResponseLocation { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
