namespace Condiva.Api.Common.Idempotency;

public static class IdempotencyHeaders
{
    public const string Key = "Idempotency-Key";
    public const string Replayed = "Idempotency-Replayed";
}
