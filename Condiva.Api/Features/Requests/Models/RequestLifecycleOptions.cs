namespace Condiva.Api.Features.Requests.Models;

public sealed class RequestLifecycleOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;
    public int RequestReopenWindowDays { get; set; } = 7;
    public int RequestReopenDurationDays { get; set; } = 7;
    public int TerminalCleanupAfterDays { get; set; } = 30;
    public int OfferReopenWindowDays { get; set; } = 7;
    public int OfferCleanupAfterDays { get; set; } = 30;
}
