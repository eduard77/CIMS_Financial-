namespace Financials.Domain.Common;

/// <summary>
/// Marker for entities whose persistence carries the four audit columns
/// (CLAUDE.md §8). The <see cref="Financials.Infrastructure.Persistence.AuditingSaveChangesInterceptor"/>
/// fills these on Add and Update; domain code never assigns them by hand.
/// See ADR-0004 for the full rationale.
/// </summary>
public interface IAuditable
{
    DateTime CreatedAt { get; }

    string CreatedByUserId { get; }

    DateTime UpdatedAt { get; }

    string UpdatedByUserId { get; }
}
