using System.Diagnostics.CodeAnalysis;
using Financials.Application.ChangeEvents;
using Financials.Domain.ChangeEvents;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.ChangeEvents;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container.")]
internal sealed class ChangeEventRepository : IChangeEventRepository
{
    private readonly FinancialsDbContext _db;

    public ChangeEventRepository(FinancialsDbContext db)
    {
        _db = db;
    }

    public Task<ChangeEvent?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        => _db.ChangeEvents.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ChangeEvent>> ListByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken)
        => await _db.ChangeEvents
            .AsNoTracking()
            .Where(c => c.FinancialsProjectId == financialsProjectId)
            .OrderByDescending(c => c.NotifiedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> ReferenceExistsAsync(
        Guid financialsProjectId,
        ChangeEventType type,
        string reference,
        CancellationToken cancellationToken)
        => _db.ChangeEvents
            .AsNoTracking()
            .AnyAsync(c =>
                    c.FinancialsProjectId == financialsProjectId
                    && c.Type == type
                    && c.Reference == reference,
                cancellationToken);

    public void Add(ChangeEvent changeEvent)
    {
        ArgumentNullException.ThrowIfNull(changeEvent);
        _db.ChangeEvents.Add(changeEvent);
    }
}
