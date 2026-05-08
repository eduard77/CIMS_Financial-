using Financials.Domain.Commitments;

namespace Financials.Application.Commitments;

public interface ICommitmentRepository
{
    Task<Commitment?> FindByIdAsync(Guid commitmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Commitment>> ListByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken);

    Task<bool> ReferenceExistsAsync(
        Guid financialsProjectId,
        CommitmentType type,
        string reference,
        CancellationToken cancellationToken);

    void Add(Commitment commitment);
}
