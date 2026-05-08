using Financials.Application.Budgets.Boq;
using Financials.Application.Common;
using Financials.Application.Persistence;
using Financials.Domain.Common;
using FluentValidation;
using MediatR;

namespace Financials.Application.Budgets;

/// <summary>
/// F1 #1 — imports a Genera BoQ XML 1.0 document into a draft revision of
/// the project's budget. Auto-opens a draft revision using the document's
/// RevisionReason if no draft is currently open.
/// </summary>
public sealed record ImportBoqCommand(string XmlContent) : IRequest<Result<ImportBoqResult>>;

public sealed record ImportBoqResult(
    Guid BudgetId,
    Guid BudgetRevisionId,
    int LinesImported,
    IReadOnlyList<string> Errors);

public sealed class ImportBoqValidator : AbstractValidator<ImportBoqCommand>
{
    public ImportBoqValidator()
    {
        RuleFor(x => x.XmlContent).NotEmpty();
    }
}

public sealed class ImportBoqCommandHandler : IRequestHandler<ImportBoqCommand, Result<ImportBoqResult>>
{
    private readonly IBudgetRepository _budgets;
    private readonly IFinancialsDbContext _db;

    public ImportBoqCommandHandler(IBudgetRepository budgets, IFinancialsDbContext db)
    {
        _budgets = budgets;
        _db = db;
    }

    public async Task<Result<ImportBoqResult>> Handle(
        ImportBoqCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parse = BoqXmlParser.Parse(request.XmlContent);
        if (!parse.IsValid || parse.Document is null)
        {
            return Result<ImportBoqResult>.Failure(
                $"BoQ XML failed to parse: {string.Join("; ", parse.Errors)}");
        }

        var doc = parse.Document;

        var budget = await _budgets
            .FindByFinancialsProjectIdAsync(doc.FinancialsProjectId, cancellationToken)
            .ConfigureAwait(false);

        if (budget is null)
        {
            return Result<ImportBoqResult>.Failure(
                $"No budget exists for project {doc.FinancialsProjectId}. Create a budget first.");
        }

        if (doc.Currency is { } currency
            && !string.Equals(currency, budget.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ImportBoqResult>.Failure(
                $"BoQ currency '{currency}' does not match budget currency '{budget.Currency}'.");
        }

        var draft = budget.CurrentDraft();
        if (draft is null)
        {
            try
            {
                draft = budget.OpenRevision(doc.RevisionReason);
            }
            catch (InvalidOperationException ex)
            {
                return Result<ImportBoqResult>.Failure(ex.Message);
            }
        }

        var lineErrors = new List<string>();
        var existingLineNumbers = draft.Lines.Select(l => l.LineNumber).ToHashSet();
        var imported = 0;

        foreach (var line in doc.Lines)
        {
            if (existingLineNumbers.Contains(line.LineNumber))
            {
                lineErrors.Add($"Line {line.LineNumber} already exists in the draft revision.");
                continue;
            }

            try
            {
                draft.AddLine(
                    line.LineNumber,
                    line.CimsCostCodeId,
                    line.Description,
                    line.Quantity,
                    line.UnitOfMeasure,
                    new Money(line.UnitRate, budget.Currency),
                    line.WorkPackage,
                    activityId: null);
                existingLineNumbers.Add(line.LineNumber);
                imported++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                lineErrors.Add($"Line {line.LineNumber}: {ex.Message}");
            }
        }

        if (imported > 0)
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<ImportBoqResult>.Success(new ImportBoqResult(
            budget.Id,
            draft.Id,
            imported,
            lineErrors));
    }
}
