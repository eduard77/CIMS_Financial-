using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using Financials.Domain.Projects;
using FluentValidation;
using MediatR;

namespace Financials.Application.Projects;

/// <summary>
/// Configures (or updates) the Financials commercial overlay for a project
/// per ADR-0005. F0 item 3 (contract template selection); also persists
/// retention scheme + payment terms; from Sprint 6 also persists the F2
/// over-commitment policy (ADR-0009).
/// </summary>
public sealed record ConfigureProjectCommercialSetupCommand(
    Guid FinancialsProjectId,
    Guid ContractTemplateId,
    decimal RetentionPercentage,
    decimal RetentionReleaseAtPCPercentage,
    decimal RetentionReleaseAtDLPEndPercentage,
    int PaymentNetDays,
    int PaymentCycleDays,
    int? PaymentDueDayOfMonth,
    OverCommitmentMode OverCommitmentMode = OverCommitmentMode.Warn,
    decimal OverCommitmentToleranceAmount = 0m,
    string OverCommitmentToleranceCurrency = Money.DefaultCurrency,
    int Nec4PmAcknowledgementDays = 7,
    int Nec4ContractorQuotationDays = 21,
    int Nec4PmAssessmentDays = 14,
    int Nec4EarlyWarningResponseDays = 7)
    : IRequest<Result<Guid>>;

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
        RuleFor(x => x.OverCommitmentMode).IsInEnum();
        RuleFor(x => x.OverCommitmentToleranceAmount).GreaterThanOrEqualTo(0m);
        RuleFor(x => x.OverCommitmentToleranceCurrency).NotEmpty().Length(3);
        RuleFor(x => x.Nec4PmAcknowledgementDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Nec4ContractorQuotationDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Nec4PmAssessmentDays).InclusiveBetween(1, 365);
        RuleFor(x => x.Nec4EarlyWarningResponseDays).InclusiveBetween(1, 365);
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
            return Result<Guid>.Failure(
                "CIMS is currently unavailable. Try again in a moment.");
        }

        if (templates.All(t => t.Id != request.ContractTemplateId))
        {
            return Result<Guid>.Failure(
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

        OverCommitmentPolicy policy;
        Nec4SlaPolicy slaPolicy;
        try
        {
            policy = OverCommitmentPolicy.Create(
                request.OverCommitmentMode,
                new Money(
                    request.OverCommitmentToleranceAmount,
                    request.OverCommitmentToleranceCurrency));
            slaPolicy = Nec4SlaPolicy.Create(
                request.Nec4PmAcknowledgementDays,
                request.Nec4ContractorQuotationDays,
                request.Nec4PmAssessmentDays,
                request.Nec4EarlyWarningResponseDays);
        }
        catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
        {
            return Result<Guid>.Failure(ex.Message);
        }

        if (existing is null)
        {
            var config = ProjectCommercialConfiguration.Configure(
                request.FinancialsProjectId,
                request.ContractTemplateId,
                retention,
                paymentTerms,
                policy,
                slaPolicy);
            _configs.Add(config);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result<Guid>.Success(config.Id);
        }

        existing.UpdateConfiguration(request.ContractTemplateId, retention, paymentTerms, policy, slaPolicy);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result<Guid>.Success(existing.Id);
    }
}
