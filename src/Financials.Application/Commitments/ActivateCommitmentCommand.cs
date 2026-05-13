using System.Globalization;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Projects;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Financials.Application.Commitments;

public sealed record ActivateCommitmentCommand(Guid CommitmentId) : IRequest<Result>;

public sealed class ActivateCommitmentValidator : AbstractValidator<ActivateCommitmentCommand>
{
    public ActivateCommitmentValidator()
    {
        RuleFor(x => x.CommitmentId).NotEmpty();
    }
}

public sealed class ActivateCommitmentCommandHandler : IRequestHandler<ActivateCommitmentCommand, Result>
{
    private readonly ICommitmentRepository _commitments;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;
    private readonly IOverCommitmentEvaluator _evaluator;
    private readonly ILogger<ActivateCommitmentCommandHandler> _logger;

    public ActivateCommitmentCommandHandler(
        ICommitmentRepository commitments,
        IFinancialsDbContext db,
        ICurrentUserService currentUser,
        IClock clock,
        IOverCommitmentEvaluator evaluator,
        ILogger<ActivateCommitmentCommandHandler> logger)
    {
        _commitments = commitments;
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _evaluator = evaluator;
        _logger = logger;
    }

    public async Task<Result> Handle(ActivateCommitmentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result.Failure("An authenticated user is required to activate a commitment.");
        }

        var commitment = await _commitments.FindByIdAsync(request.CommitmentId, cancellationToken).ConfigureAwait(false);
        if (commitment is null)
        {
            return Result.Failure($"Commitment {request.CommitmentId} not found.");
        }

        var evaluation = await _evaluator
            .EvaluateAsync(request.CommitmentId, cancellationToken)
            .ConfigureAwait(false);

        if (evaluation.HasBreaches)
        {
            if (evaluation.Mode == OverCommitmentMode.HardBlock)
            {
                return Result.Failure(BuildBreachMessage(evaluation, hardBlocked: true));
            }
            if (evaluation.Mode == OverCommitmentMode.Warn)
            {
                LogBreachWarning(commitment.Reference, evaluation);
            }
        }

        try
        {
            commitment.Activate(_currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result.Failure(ex.Message);
        }
    }

    private void LogBreachWarning(string commitmentReference, OverCommitmentEvaluation evaluation)
    {
        foreach (var breach in evaluation.Breaches)
        {
            _logger.LogWarning(
                "Over-commitment (Warn mode) on commitment {CommitmentReference}: " +
                "cost code {CostCodeId} budget {BudgetApproved} + tolerance {Tolerance} " +
                "vs other-active {OtherActive} + this {ThisCommitment} (breach {BreachAmount}).",
                commitmentReference,
                breach.CimsCostCodeId,
                breach.BudgetApproved,
                evaluation.Tolerance,
                breach.OtherActiveCommitments,
                breach.ThisCommitment,
                breach.BreachAmount);
        }
    }

    private static string BuildBreachMessage(OverCommitmentEvaluation evaluation, bool hardBlocked)
    {
        var prefix = hardBlocked
            ? "Activation blocked: this commitment exceeds the budget envelope on "
            : "Over-commitment detected on ";
        var detail = string.Join(
            "; ",
            evaluation.Breaches.Select(b => string.Format(
                CultureInfo.InvariantCulture,
                "cost code {0:N} (over by {1})",
                b.CimsCostCodeId,
                b.BreachAmount)));
        return $"{prefix}{evaluation.Breaches.Count} cost code(s): {detail}.";
    }
}
