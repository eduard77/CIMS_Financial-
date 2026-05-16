namespace Financials.Infrastructure.Outbox;

/// <summary>
/// Tunables for the outbox dispatcher background service (ADR-0002).
/// Bound from the <c>Outbox</c> configuration section.
///
/// Plan §4 mandates "retry indefinitely with backoff" for transient failures —
/// there is no <c>MaxAttempts</c> knob. Permanent failures (transport returns
/// <c>PermanentFailure</c>) and poison messages (transport throws) are the
/// only paths to terminal <c>Failed</c>.
/// </summary>
public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the dispatcher polls for new pending rows. Default 5 s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Max rows claimed per poll cycle. Default 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Backoff applied after the first transient failure. Doubles on each
    /// subsequent failure (2x, 4x, 8x, ...), capped at <see cref="MaxBackoff"/>.
    /// Default 5 seconds.
    /// </summary>
    public TimeSpan BaseBackoff { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Upper bound on the exponential backoff. Default 5 minutes. Plan §4
    /// caps the backoff interval, not the retry count.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Set <c>true</c> in tests that drive the dispatcher manually via
    /// <see cref="OutboxDispatcherService.RunOnceAsync"/> to suppress the
    /// background poll loop. Production keeps this <c>false</c>.
    /// </summary>
    public bool DisableBackgroundLoop { get; set; }
}
