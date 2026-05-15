using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Common.Authorization;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using Financials.Domain.Projects;
using FluentValidation;
using MediatR;

namespace Financials.Application.Projects;

/// <summary>
/// Configures (or updates) the Financials commercial overlay for a project
/// per ADR-0005. F0 item 3 (contract template selection); also persists
/// retention scheme + payment terms.
/// </summary>
[RequiresPermission(AuthorizationPolicies.SetupConfigure)]
public sealed record ConfigureProjectCommercialSetupCommand(
    Guid FinancialsProjectId,
    Guid ContractTemplateId,
    decimal RetentionPercentage,
    decimal RetentionReleaseAtPCPercentage,
    decimal RetentionReleaseAtDLPEndPercentage,
    int PaymentNetDays,
    int PaymentCycleDays,
    int? PaymentDueDayOfMonth,
    OverCommitmentGuardMode OverCommitmentGuardMode = OverCommitmentGuardMode.Warn) : IRequest<Result<Guid>>;

public sealed class ConfigureProjectCommercialSetupValidator
    : AbstractValidator<ConfigureProjectCommercialSetupCommand>
{
    public ConfigureProjectCommercialSetupValidator()
    {
        RuleFor(x => x.FinancialsProjectId).NotEmpty();
        RuleFor(x => x.ContractTemplateId).NotEmpty()
            .WithMessage("A contract template must be selected.");
        RuleFor(x => x.RetentionPercentage).InclusiveBetween(0m, 100m);
        RuleFor(x => x.RetentionReleaseAtPCPercentage).InclusiveBetween(0m, 100m);
        RuleFor(x => x.RetentionReleaseAtDLPEndPercentage).InclusiveBetween(0m, 100m);
        RuleFor(x => x)
            .Must(c => c.RetentionReleaseAtPCPercentage + c.RetentionReleaseAtDLPEndPercentage == 100m)
            .WithMessage("Retention release at PC and at DLP end must sum to 100%.");
        RuleFor(x => x.PaymentNetDays).GreaterThan(0);
        RuleFor(x => x.PaymentCycleDays).GreaterThan(0);
        RuleFor(x => x.PaymentDueDayOfMonth).InclusiveBetween(1, 31)
            .When(x => x.PaymentDueDayOfMonth.HasValue);
    }
}

public sealed class ConfigureProjectCommercialSetupCommandHandler
    : IRequestHandler<ConfigureProjectCommercialSetupCommand, Result<Guid>>
{
    private readonly IFinancialsProjectRepository _projects;
    private readonly IProjectCommercialConfigurationRepository _configs;
    private readonly ICimsClient _cims;
    private readonly IFinancialsDbContext _db;

    public ConfigureProjectCommercialSetupCommandHandler(
        IFinancialsProjectRepository projects,
        IProjectCommercialConfigurationRepository configs,
        ICimsClient cims,
        IFinancialsDbContext db)
    {
        _projects = projects;
        _configs = configs;
        _cims = cims;
        _db = db;
    }

    public async Task<Result<Guid>> Handle(
        ConfigureProjectCommercialSetupCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Find the FinancialsProject by its CIMS project id (the FK we store).
        // Sprint 2 simplification: the command carries FinancialsProjectId directly.
        // FinancialsProject lookup is by CIMS project id, so the caller (UI) passes
        // the local FinancialsProject.Id; we trust it after the [Authorize] gate.
        var existing = await _configs
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ContractTemplateSummary> templates;
        try
        {
            templates = await _cims
                .ListContractTemplatesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Result<Guid>.DependencyUnavailable(
                "CIMS is currently unavailable. Try again in a moment.");
        }

        if (templates.All(t => t.Id != request.ContractTemplateId))
        {
            return Result<Guid>.Failure(FailureReason.ValidationFailed,
                $"Contract template {request.ContractTemplateId} is not in the CIMS catalog.");
        }

        var retention = RetentionScheme.Create(
            request.RetentionPercentage,
            request.RetentionReleaseAtPCPercentage,
            request.RetentionReleaseAtDLPEndPercentage);
        var paymentTerms = PaymentTerms.Create(
            request.PaymentNetDays,
            request.PaymentCycleDays,
            request.PaymentDueDayOfMonth);
        var guard = new OverCommitmentGuard(request.OverCommitmentGuardMode);

        if (existing is null)
        {
            var config = ProjectCommercialConfiguration.Configure(
                request.FinancialsProjectId,
                request.ContractTemplateId,
                retention,
                paymentTerms,
                guard);
            _configs.Add(config);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(config.Id);
        }

        existing.UpdateConfiguration(request.ContractTemplateId, retention, paymentTerms, guard);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Guid>.Success(existing.Id);
    }
}
