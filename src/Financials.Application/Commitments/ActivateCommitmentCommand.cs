using Financials.Application.Budgets;
using Financials.Application.Common;
using Financials.Application.Common.Authorization;
using Financials.Application.Persistence;
using Financials.Application.Projects;
using Financials.Domain.Budgets;
using Financials.Domain.Commitments;
using Financials.Domain.Common;
using Financials.Domain.Projects;
using FluentValidation;
using MediatR;

namespace Financials.Application.Commitments;

[RequiresPermission(AuthorizationPolicies.CommitmentsWrite)]
public sealed record ActivateCommitmentCommand(Guid CommitmentId) : IRequest<Result<ActivateCommitmentResult>>;

public sealed record ActivateCommitmentResult(
    Guid CommitmentId,
    IReadOnlyList<string> Warnings);

public sealed class ActivateCommitmentValidator : AbstractValidator<ActivateCommitmentCommand>
{
    public ActivateCommitmentValidator()
    {
        RuleFor(x => x.CommitmentId).NotEmpty();
    }
}

public sealed class ActivateCommitmentCommandHandler
    : IRequestHandler<ActivateCommitmentCommand, Result<ActivateCommitmentResult>>
{
    private readonly ICommitmentRepository _commitments;
    private readonly IBudgetRepository _budgets;
    private readonly IProjectCommercialConfigurationRepository _configs;
    private readonly IFinancialsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IClock _clock;

    public ActivateCommitmentCommandHandler(
        ICommitmentRepository commitments,
        IBudgetRepository budgets,
        IProjectCommercialConfigurationRepository configs,
        IFinancialsDbContext db,
        ICurrentUserService currentUser,
        IClock clock)
    {
        _commitments = commitments;
        _budgets = budgets;
        _configs = configs;
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<ActivateCommitmentResult>> Handle(
        ActivateCommitmentCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(_currentUser.UserId))
        {
            return Result<ActivateCommitmentResult>.Unauthorized(
                "An authenticated user is required to activate a commitment.");
        }

        var commitment = await _commitments.FindByIdAsync(request.CommitmentId, cancellationToken).ConfigureAwait(false);
        if (commitment is null)
        {
            return Result<ActivateCommitmentResult>.NotFound($"Commitment {request.CommitmentId} not found.");
        }

        var guardMode = OverCommitmentGuardMode.Warn;
        var pcc = await _configs.FindByFinancialsProjectIdAsync(commitment.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (pcc is not null)
        {
            guardMode = pcc.OverCommitmentGuard.Mode;
        }

        var breaches = await ComputeBreachesAsync(commitment, cancellationToken).ConfigureAwait(false);

        if (breaches.Count > 0 && guardMode == OverCommitmentGuardMode.HardBlock)
        {
            return Result<ActivateCommitmentResult>.PreconditionFailed(
                "Activation blocked by over-commitment guard (HardBlock mode). Breached cost codes: "
                + string.Join("; ", breaches.Select(b =>
                    $"{b.CimsCostCodeId} (budget {b.Budget:N2}, committed {b.AlreadyCommitted:N2}, this {b.ThisCommitment:N2}, over by {b.OverBy:N2})")));
        }

        try
        {
            commitment.Activate(_currentUser.UserId, _clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return Result<ActivateCommitmentResult>.Failure(ex.Reason, ex.Message);
        }

        var warnings = breaches
            .Select(b => $"Over-commitment warning on cost code {b.CimsCostCodeId}: budget {b.Budget:N2}, committed {b.AlreadyCommitted + b.ThisCommitment:N2} ({b.OverBy:N2} over).")
            .ToList();

        return Result<ActivateCommitmentResult>.Success(
            new ActivateCommitmentResult(commitment.Id, warnings));
    }

    private async Task<IReadOnlyList<OverCommitBreach>> ComputeBreachesAsync(
        Commitment commitment,
        CancellationToken cancellationToken)
    {
        var budget = await _budgets.FindByFinancialsProjectIdAsync(commitment.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (budget is null)
        {
            // No budget yet — nothing to compare against; surface as a single warning later.
            return commitment.Lines.Count == 0
                ? Array.Empty<OverCommitBreach>()
                : commitment.Lines
                    .GroupBy(l => l.CimsCostCodeId)
                    .Select(g => new OverCommitBreach(
                        g.Key, 0m, 0m, g.Sum(l => l.Value.Amount), g.Sum(l => l.Value.Amount)))
                    .ToList();
        }

        var latestApproved = budget.LatestApproved();
        if (latestApproved is null)
        {
            return Array.Empty<OverCommitBreach>();
        }

        var budgetByCostCode = latestApproved.Lines
            .GroupBy(l => l.CimsCostCodeId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount.Amount));

        var existingActiveCommitments = await _commitments
            .ListByFinancialsProjectIdAsync(commitment.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        var alreadyCommittedByCostCode = existingActiveCommitments
            .Where(c => c.Status == CommitmentStatus.Active && c.Id != commitment.Id)
            .SelectMany(c => c.Lines)
            .GroupBy(l => l.CimsCostCodeId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Value.Amount));

        var thisByCostCode = commitment.Lines
            .GroupBy(l => l.CimsCostCodeId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Value.Amount));

        var breaches = new List<OverCommitBreach>();
        foreach (var (costCode, thisValue) in thisByCostCode)
        {
            var budgetValue = budgetByCostCode.GetValueOrDefault(costCode, 0m);
            var alreadyCommitted = alreadyCommittedByCostCode.GetValueOrDefault(costCode, 0m);
            var totalAfter = alreadyCommitted + thisValue;
            if (totalAfter > budgetValue)
            {
                breaches.Add(new OverCommitBreach(
                    costCode,
                    budgetValue,
                    alreadyCommitted,
                    thisValue,
                    totalAfter - budgetValue));
            }
        }

        return breaches;
    }

    private sealed record OverCommitBreach(
        Guid CimsCostCodeId,
        decimal Budget,
        decimal AlreadyCommitted,
        decimal ThisCommitment,
        decimal OverBy);
}
