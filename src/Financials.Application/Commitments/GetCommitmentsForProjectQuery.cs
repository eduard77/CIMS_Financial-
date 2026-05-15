using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Domain.Commitments;
using MediatR;

namespace Financials.Application.Commitments;

public sealed record GetCommitmentsForProjectQuery(Guid FinancialsProjectId)
    : IRequest<Result<IReadOnlyList<CommitmentDto>>>;

public sealed record CommitmentDto(
    Guid Id,
    Guid FinancialsProjectId,
    CommitmentType Type,
    string Reference,
    Guid CounterpartyCimsOrganisationId,
    string CounterpartyName,
    CommitmentStatus Status,
    string Currency,
    decimal TotalValue,
    int LineCount,
    DateTime? ActivatedAt,
    DateTime? ClosedAt);

public sealed class GetCommitmentsForProjectQueryHandler
    : IRequestHandler<GetCommitmentsForProjectQuery, Result<IReadOnlyList<CommitmentDto>>>
{
    private readonly ICommitmentRepository _commitments;
    private readonly ICimsClient _cims;

    public GetCommitmentsForProjectQueryHandler(ICommitmentRepository commitments, ICimsClient cims)
    {
        _commitments = commitments;
        _cims = cims;
    }

    public async Task<Result<IReadOnlyList<CommitmentDto>>> Handle(
        GetCommitmentsForProjectQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var commitments = await _commitments
            .ListByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (commitments.Count == 0)
        {
            return Result<IReadOnlyList<CommitmentDto>>.Success(Array.Empty<CommitmentDto>());
        }

        var dtos = new List<CommitmentDto>(commitments.Count);
        try
        {
            foreach (var c in commitments)
            {
                var counterparty = await _cims
                    .GetOrganisationAsync(c.CounterpartyCimsOrganisationId, cancellationToken)
                    .ConfigureAwait(false);
                dtos.Add(new CommitmentDto(
                    c.Id,
                    c.FinancialsProjectId,
                    c.Type,
                    c.Reference,
                    c.CounterpartyCimsOrganisationId,
                    counterparty?.Name ?? "(unknown — CIMS lookup returned no record)",
                    c.Status,
                    c.Currency,
                    c.TotalValue.Amount,
                    c.Lines.Count,
                    c.ActivatedAt,
                    c.ClosedAt));
            }
        }
        catch (HttpRequestException)
        {
            return Result<IReadOnlyList<CommitmentDto>>.DependencyUnavailable(
                "CIMS is currently unavailable. Some counterparty details cannot be displayed.");
        }

        return Result<IReadOnlyList<CommitmentDto>>.Success(dtos);
    }
}
