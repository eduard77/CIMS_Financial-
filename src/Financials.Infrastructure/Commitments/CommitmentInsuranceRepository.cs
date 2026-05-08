using System.Diagnostics.CodeAnalysis;
using Financials.Application.Commitments;
using Financials.Domain.Commitments;
using Financials.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Financials.Infrastructure.Commitments;

[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Resolved by the DI container.")]
internal sealed class CommitmentInsuranceRepository : ICommitmentInsuranceRepository
{
    private readonly FinancialsDbContext _db;

    public CommitmentInsuranceRepository(FinancialsDbContext db)
    {
        _db = db;
    }

    public Task<CommitmentInsurance?> FindByIdAsync(Guid insuranceId, CancellationToken cancellationToken)
        => _db.CommitmentInsurances.FirstOrDefaultAsync(i => i.Id == insuranceId, cancellationToken);

    public async Task<IReadOnlyList<CommitmentInsurance>> ListByCommitmentIdAsync(
        Guid commitmentId,
        CancellationToken cancellationToken)
        => await _db.CommitmentInsurances
            .AsNoTracking()
            .Where(i => i.CommitmentId == commitmentId)
            .OrderBy(i => i.ExpiresAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<CommitmentInsurance>> ListActiveByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken)
        => await (from i in _db.CommitmentInsurances.AsNoTracking()
                  join c in _db.Commitments on i.CommitmentId equals c.Id
                  where c.FinancialsProjectId == financialsProjectId
                        && i.Status == InsuranceStatus.Active
                  orderby i.ExpiresAt
                  select i)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(CommitmentInsurance insurance)
    {
        ArgumentNullException.ThrowIfNull(insurance);
        _db.CommitmentInsurances.Add(insurance);
    }
}
