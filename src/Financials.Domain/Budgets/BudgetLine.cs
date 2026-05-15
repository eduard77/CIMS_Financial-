using Financials.Domain.Common;

namespace Financials.Domain.Budgets;

/// <summary>
/// One line in a <see cref="BudgetRevision"/>. References a CIMS-owned cost
/// code by id (ADR-0005); <see cref="Description"/> is captured at line
/// creation as the BoQ snapshot text — see ADR-0006 §Decision.
/// </summary>
public sealed class BudgetLine
{
    public Guid Id { get; private set; }

    public Guid BudgetRevisionId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid CimsCostCodeId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public string UnitOfMeasure { get; private set; } = string.Empty;

    public Money UnitRate { get; private set; } = null!;

    public Money Amount { get; private set; } = null!;

    public string? WorkPackage { get; private set; }

    public Guid? ActivityId { get; private set; }

    private BudgetLine()
    {
    }

    internal static BudgetLine Create(
        Guid budgetRevisionId,
        int lineNumber,
        Guid cimsCostCodeId,
        string description,
        decimal quantity,
        string unitOfMeasure,
        Money unitRate,
        string? workPackage,
        Guid? activityId)
    {
        if (cimsCostCodeId == Guid.Empty)
        {
            throw DomainException.ValidationFailed("A CIMS cost code is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw DomainException.ValidationFailed("Description is required.");
        }

        if (string.IsNullOrWhiteSpace(unitOfMeasure))
        {
            throw DomainException.ValidationFailed("Unit of measure is required.");
        }

        if (quantity < 0m)
        {
            throw DomainException.ValidationFailed("Quantity must be non-negative.");
        }

        if (lineNumber <= 0)
        {
            throw DomainException.ValidationFailed("Line number must be positive.");
        }

        if (unitRate is null)
        {
            throw DomainException.ValidationFailed("Unit rate is required.");
        }

        return new BudgetLine
        {
            Id = Guid.NewGuid(),
            BudgetRevisionId = budgetRevisionId,
            LineNumber = lineNumber,
            CimsCostCodeId = cimsCostCodeId,
            Description = description,
            Quantity = quantity,
            UnitOfMeasure = unitOfMeasure,
            UnitRate = unitRate,
            Amount = unitRate.Multiply(quantity),
            WorkPackage = workPackage,
            ActivityId = activityId,
        };
    }
}
