namespace Financials.Infrastructure.Inbox;

public sealed class CimsWebhookOptions
{
    public const string SectionName = "Cims:Webhook";

    /// <summary>
    /// Per-spoke shared secret used to verify HMAC-SHA256 signatures on inbound
    /// CIMS events (ADR-0007). Required at startup; configure via user-secrets
    /// in development and via the deployment secret store in other environments.
    /// </summary>
    public string Secret { get; set; } = string.Empty;
}
