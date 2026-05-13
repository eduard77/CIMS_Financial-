using System.Diagnostics.CodeAnalysis;
using Financials.Application.Commitments.Securities;
using Financials.Domain.Commitments;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.Commitments;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container.")]
internal sealed class CommitmentSecurityRepository : ICommitmentSecurityRepository
{
    private readonly FinancialsDbContext _db;

    public CommitmentSecurityRepository(FinancialsDbContext db)
    {
        _db = db;
    }

    public Task<CommitmentSecurity?> FindByIdAsync(Guid securityId, CancellationToken cancellationToken)
        => _db.CommitmentSecurities.FirstOrDefaultAsync(s => s.Id == securityId, cancellationToken);

    public async Task<IReadOnlyList<CommitmentSecurity>> ListByCommitmentIdAsync(
        Guid commitmentId,
        CancellationToken cancellationToken)
        => await _db.CommitmentSecurities
            .AsNoTracking()
            .Where(s => s.CommitmentId == commitmentId)
            .OrderBy(s => s.Type)
            .ThenBy(s => s.ExpiresOn)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CommitmentSecurity>> ListByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken)
    {
        var query =
            from s in _db.CommitmentSecurities.AsNoTracking()
            join c in _db.Commitments.AsNoTracking() on s.CommitmentId equals c.Id
            where c.FinancialsProjectId == financialsProjectId
            orderby s.ExpiresOn
            select s;

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ReferenceExistsAsync(
        Guid commitmentId,
        SecurityType type,
        string reference,
        CancellationToken cancellationToken)
        => _db.CommitmentSecurities
            .AsNoTracking()
            .AnyAsync(s =>
                    s.CommitmentId == commitmentId
                    && s.Type == type
                    && s.Reference == reference,
                cancellationToken);

    public void Add(CommitmentSecurity security)
    {
        ArgumentNullException.ThrowIfNull(security);
        _db.CommitmentSecurities.Add(security);
    }
}
