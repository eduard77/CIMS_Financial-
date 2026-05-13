using Financials.Domain.ChangeEvents;

namespace Financials.Application.ChangeEvents;

public interface IChangeEventRepository
{
    Task<ChangeEvent?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChangeEvent>> ListByFinancialsProjectIdAsync(
        Guid financialsProjectId,
        CancellationToken cancellationToken);

    Task<bool> ReferenceExistsAsync(
        Guid financialsProjectId,
        ChangeEventType type,
        string reference,
        CancellationToken cancellationToken);

    void Add(ChangeEvent changeEvent);
}
