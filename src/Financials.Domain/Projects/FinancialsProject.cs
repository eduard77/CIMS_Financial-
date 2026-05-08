using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;

namespace Financials.Domain.Projects;

/// <summary>
/// A CIMS project that has been confirmed for use in Financials. The CIMS
/// project itself is the source of truth (CLAUDE.md §2 #4); this aggregate
/// only records *that* a project is in scope locally and *when* it was
/// brought into scope. Reads of name, parties, dates etc. always go back
/// to CIMS via Pattern A (ADR-0001, ADR-0002).
/// </summary>
public sealed class FinancialsProject : IAuditable
{
    public Guid Id { get; private set; }

    public Guid CimsProjectId { get; private set; }

    public DateTime ConfirmedAt { get; private set; }

    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "EF Core requires byte[] for SQL Server rowversion concurrency tokens.")]
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; private set; }

    public string CreatedByUserId { get; private set; } = string.Empty;

    public DateTime UpdatedAt { get; private set; }

    public string UpdatedByUserId { get; private set; } = string.Empty;

    private FinancialsProject()
    {
    }

    public static FinancialsProject Confirm(Guid cimsProjectId, DateTime confirmedAt)
    {
        if (cimsProjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "A CIMS project id is required to confirm a project for Financials.",
                nameof(cimsProjectId));
        }

        return new FinancialsProject
        {
            Id = Guid.NewGuid(),
            CimsProjectId = cimsProjectId,
            ConfirmedAt = DateTime.SpecifyKind(confirmedAt, DateTimeKind.Utc),
        };
    }
}
