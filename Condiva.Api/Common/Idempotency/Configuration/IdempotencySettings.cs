namespace Condiva.Api.Common.Idempotency.Configuration;

public sealed class IdempotencySettings
{
    public bool Enabled { get; set; } = true;
    public int ReplayTtlHours { get; set; } = 24;
    public int MinKeyLength { get; set; } = 8;
    public int MaxKeyLength { get; set; } = 128;
}
