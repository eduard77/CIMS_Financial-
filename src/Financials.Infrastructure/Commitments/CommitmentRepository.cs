using System.Diagnostics.CodeAnalysis;
using Financials.Application.Commitments;
using Financials.Domain.Commitments;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.Commitments;

internal sealed class CommitmentRepository : ICommitmentRepository
{
    private readonly FinancialsDbContext _db;

    public CommitmentRepository(FinancialsDbContext db)
    {
        _db = db;
    }

    public Task<Commitment?> FindByIdAsync(Guid commitmentId, CancellationToken cancellationToken)
        => _db.Commitments
            .Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.Id == commitmentId, cancellationToken);

    public async Task<IReadOnlyList<Commitment>> ListByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken)
        => await _db.Commitments
            .AsNoTracking()
            .Include(c => c.Lines)
            .Where(c => c.FinancialsProjectId == financialsProjectId)
            .OrderBy(c => c.Reference)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> ReferenceExistsAsync(
        Guid financialsProjectId,
        CommitmentType type,
        string reference,
        CancellationToken cancellationToken)
        => _db.Commitments
            .AsNoTracking()
            .AnyAsync(c =>
                    c.FinancialsProjectId == financialsProjectId
                    && c.Type == type
                    && c.Reference == reference,
                cancellationToken);

    public void Add(Commitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        _db.Commitments.Add(commitment);
    }
}
