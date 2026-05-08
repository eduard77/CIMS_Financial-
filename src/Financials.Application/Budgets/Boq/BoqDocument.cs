namespace Financials.Application.Budgets.Boq;

/// <summary>
/// Parsed Genera BoQ XML 1.0 document. Schema lives in
/// Financials.Contracts.Schemas/genera-boq-1.0.xsd.
/// </summary>
public sealed record BoqDocument(
    Guid FinancialsProjectId,
    string RevisionReason,
    string? Currency,
    IReadOnlyList<BoqDocumentLine> Lines);

public sealed record BoqDocumentLine(
    int LineNumber,
    Guid CimsCostCodeId,
    string Description,
    decimal Quantity,
    string UnitOfMeasure,
    decimal UnitRate,
    string? WorkPackage,
    string? Nrm2Group);
