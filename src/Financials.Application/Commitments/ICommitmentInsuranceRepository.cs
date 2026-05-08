using Financials.Domain.Commitments;

namespace Financials.Application.Commitments;

public interface ICommitmentInsuranceRepository
{
    Task<CommitmentInsurance?> FindByIdAsync(Guid insuranceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CommitmentInsurance>> ListByCommitmentIdAsync(
        Guid commitmentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CommitmentInsurance>> ListActiveByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken);

    void Add(CommitmentInsurance insurance);
}
