using Financials.Domain.Commitments;

namespace Financials.Application.Commitments.Securities;

public interface ICommitmentSecurityRepository
{
    Task<CommitmentSecurity?> FindByIdAsync(Guid securityId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CommitmentSecurity>> ListByCommitmentIdAsync(
        Guid commitmentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CommitmentSecurity>> ListByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken);

    Task<bool> ReferenceExistsAsync(
        Guid commitmentId,
        SecurityType type,
        string reference,
        CancellationToken cancellationToken);

    void Add(CommitmentSecurity security);
}
