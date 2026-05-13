namespace Financials.Domain.Projects;

/// <summary>
/// Per-project NEC4 SLA periods (ADR-0011). Defaults match the NEC4 ECC
/// standard form expressed in calendar days; Z-clauses or Options can
/// override per project. The aggregate does <b>not</b> block transitions
/// when a clock has expired — Sprint 7 surfaces breaches as a read-side
/// chip per the user decision recorded in ADR-0011.
/// </summary>
public sealed record Nec4SlaPolicy
{
    public int PmAcknowledgementDays { get; }

    public int ContractorQuotationDays { get; }

    public int PmAssessmentDays { get; }

    public int EarlyWarningResponseDays { get; }

    private Nec4SlaPolicy(
        int pmAcknowledgementDays,
        int contractorQuotationDays,
        int pmAssessmentDays,
        int earlyWarningResponseDays)
    {
        PmAcknowledgementDays = pmAcknowledgementDays;
        ContractorQuotationDays = contractorQuotationDays;
        PmAssessmentDays = pmAssessmentDays;
        EarlyWarningResponseDays = earlyWarningResponseDays;
    }

    public static Nec4SlaPolicy Create(
        int pmAcknowledgementDays,
        int contractorQuotationDays,
        int pmAssessmentDays,
        int earlyWarningResponseDays)
    {
        Require(pmAcknowledgementDays, nameof(pmAcknowledgementDays));
        Require(contractorQuotationDays, nameof(contractorQuotationDays));
        Require(pmAssessmentDays, nameof(pmAssessmentDays));
        Require(earlyWarningResponseDays, nameof(earlyWarningResponseDays));

        return new Nec4SlaPolicy(
            pmAcknowledgementDays,
            contractorQuotationDays,
            pmAssessmentDays,
            earlyWarningResponseDays);
    }

    /// <summary>
    /// NEC4 ECC standard-form defaults (1 / 3 / 2 / 1 weeks expressed as
    /// calendar days). Match what most projects start with before Z-clauses;
    /// the QS retains the contract-interpretation responsibility per
    /// ADR-0011 §Per-project SLA policy.
    /// </summary>
    public static Nec4SlaPolicy Default() => new(7, 21, 14, 7);

    private static void Require(int value, string name)
    {
        if (value is < 1 or > 365)
        {
            throw new ArgumentOutOfRangeException(name, value, "SLA must be between 1 and 365 calendar days.");
        }
    }
}
