namespace Financials.Application.Cims;

/// <summary>
/// CIMS-owned UK tax setup for a project. Sprint 2 F0 item 2 — read via Pattern A
/// and surfaced read-only in Project Setup; consumed by F4 (AFP) and F5 (subcontract
/// administration) for VAT / CIS calculations.
/// </summary>
public sealed record ProjectTaxRegime(
    string Currency,
    IReadOnlyList<VatBand> VatBands,
    CisScope CisScope,
    bool ReverseChargeVatEnabled);

public sealed record VatBand(decimal Rate, string Label);

public enum CisScope
{
    None = 0,
    StandardRate20 = 20,
    HigherRate30 = 30,
    GrossPaymentStatus = 99,
}
