namespace Financials.Application.Common;

/// <summary>
/// Source of "now" for code that needs deterministic tests. Domain methods,
/// the audit interceptor, and any handler that needs a timestamp inject this
/// rather than calling <see cref="DateTime.UtcNow"/> directly. See ADR-0004.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
