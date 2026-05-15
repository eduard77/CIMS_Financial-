namespace Financials.Infrastructure.Outbox;

/// <summary>
/// Tunables for the outbox dispatcher background service (ADR-0002).
/// Bound from the <c>Outbox</c> configuration section.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the dispatcher polls for new pending rows. Default 5 s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Max rows claimed per poll cycle. Default 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Total attempts allowed per row before the dispatcher gives up and
    /// marks the row Failed. Default 5. A row's <c>AttemptCount</c> is
    /// compared with this after each failed attempt.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Set <c>true</c> in tests that drive the dispatcher manually via
    /// <see cref="OutboxDispatcherService.RunOnceAsync"/> to suppress the
    /// background poll loop. Production keeps this <c>false</c>.
    /// </summary>
    public bool DisableBackgroundLoop { get; set; }
}
