using Financials.Application.Cims;
using Financials.Application.Common;
using Financials.Domain.Projects;
using MediatR;

namespace Financials.Application.Projects;

public sealed record GetProjectCommercialSetupQuery(Guid FinancialsProjectId)
    : IRequest<Result<ProjectCommercialSetupDto?>>;

/// <summary>
/// Read view for the Project Setup page. Combines the Financials-owned
/// commercial overlay (ADR-0005) with the resolved CIMS contract-template
/// label. Tax regime, CBS, and role assignments are read separately by the
/// UI from <see cref="ICimsClient"/> so each section reports its own
/// loading/error state. From Sprint 6 also surfaces the F2 over-commitment
/// policy (ADR-0009).
/// </summary>
public sealed record ProjectCommercialSetupDto(
    Guid Id,
    Guid FinancialsProjectId,
    Guid ContractTemplateId,
    string ContractTemplateName,
    decimal RetentionPercentage,
    decimal RetentionReleaseAtPCPercentage,
    decimal RetentionReleaseAtDLPEndPercentage,
    int PaymentNetDays,
    int PaymentCycleDays,
    int? PaymentDueDayOfMonth,
    OverCommitmentMode OverCommitmentMode,
    decimal OverCommitmentToleranceAmount,
    string OverCommitmentToleranceCurrency);

public sealed class GetProjectCommercialSetupQueryHandler
    : IRequestHandler<GetProjectCommercialSetupQuery, Result<ProjectCommercialSetupDto?>>
{
    private readonly IProjectCommercialConfigurationRepository _configs;
    private readonly ICimsClient _cims;

    public GetProjectCommercialSetupQueryHandler(
        IProjectCommercialConfigurationRepository configs,
        ICimsClient cims)
    {
        _configs = configs;
        _cims = cims;
    }

    public async Task<Result<ProjectCommercialSetupDto?>> Handle(
        GetProjectCommercialSetupQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var config = await _configs
            .FindByFinancialsProjectIdAsync(request.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (config is null)
        {
            return Result<ProjectCommercialSetupDto?>.Success(null!);
        }

        IReadOnlyList<ContractTemplateSummary> templates;
        try
        {
            templates = await _cims
                .ListContractTemplatesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Result<ProjectCommercialSetupDto?>.Failure(
                "CIMS is currently unavailable. Some setup details cannot be displayed.");
        }

        var template = templates.FirstOrDefault(t => t.Id == config.ContractTemplateId);

        var dto = new ProjectCommercialSetupDto(
            Id: config.Id,
            FinancialsProjectId: config.FinancialsProjectId,
            ContractTemplateId: config.ContractTemplateId,
            ContractTemplateName: template?.Name ?? "(unknown — not in CIMS catalog)",
            RetentionPercentage: config.RetentionScheme.Percentage,
            RetentionReleaseAtPCPercentage: config.RetentionScheme.ReleaseAtPCPercentage,
            RetentionReleaseAtDLPEndPercentage: config.RetentionScheme.ReleaseAtDLPEndPercentage,
            PaymentNetDays: config.PaymentTerms.NetDays,
            PaymentCycleDays: config.PaymentTerms.PaymentCycleDays,
            PaymentDueDayOfMonth: config.PaymentTerms.DueDayOfMonth,
            OverCommitmentMode: config.OverCommitmentPolicy.Mode,
            OverCommitmentToleranceAmount: config.OverCommitmentPolicy.Tolerance.Amount,
            OverCommitmentToleranceCurrency: config.OverCommitmentPolicy.Tolerance.Currency);

        return Result<ProjectCommercialSetupDto?>.Success(dto);
    }
}
