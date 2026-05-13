using Financials.Application.Common;
using MediatR;

namespace Financials.Application.Commitments;

/// <summary>
/// Pre-activation evaluator (ADR-0009): the UI calls this to render the
/// "Will this activate cleanly?" banner before committing to <c>Activate</c>.
/// Returns the same <see cref="OverCommitmentEvaluation"/> the Activate
/// handler will see, so warn-mode breaches surface in the UI rather than
/// only in Serilog.
/// </summary>
public sealed record EvaluateCommitmentImpactQuery(Guid CommitmentId)
    : IRequest<Result<OverCommitmentEvaluation>>;

public sealed class EvaluateCommitmentImpactQueryHandler
    : IRequestHandler<EvaluateCommitmentImpactQuery, Result<OverCommitmentEvaluation>>
{
    private readonly IOverCommitmentEvaluator _evaluator;

    public EvaluateCommitmentImpactQueryHandler(IOverCommitmentEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    public async Task<Result<OverCommitmentEvaluation>> Handle(
        EvaluateCommitmentImpactQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var evaluation = await _evaluator
                .EvaluateAsync(request.CommitmentId, cancellationToken)
                .ConfigureAwait(false);
            return Result<OverCommitmentEvaluation>.Success(evaluation);
        }
        catch (InvalidOperationException ex)
        {
            return Result<OverCommitmentEvaluation>.Failure(ex.Message);
        }
    }
}
