using Financials.Domain.Common;

namespace Financials.Domain.Commitments;

public sealed class CommitmentLine
{
    public Guid Id { get; private set; }
    public Guid CommitmentId { get; private set; }
    public int LineNumber { get; private set; }
    public Guid CimsCostCodeId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string UnitOfMeasure { get; private set; } = string.Empty;
    public Money UnitRate { get; private set; } = null!;
    public Money Value { get; private set; } = null!;

    // EF Core requires a parameterless constructor for materialisation; not for application use.
    private CommitmentLine() { }

    internal static CommitmentLine Create(
        Guid commitmentId,
        int lineNumber,
        Guid cimsCostCodeId,
        string description,
        decimal quantity,
        string unitOfMeasure,
        Money unitRate)
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
        if (lineNumber <= 0)
        {
            throw DomainException.ValidationFailed("Line number must be positive.");
        }
        if (quantity < 0m)
        {
            throw DomainException.ValidationFailed("Quantity must be non-negative.");
        }
        if (unitRate is null)
        {
            throw DomainException.ValidationFailed("Unit rate is required.");
        }

        return new CommitmentLine
        {
            Id = Guid.NewGuid(),
            CommitmentId = commitmentId,
            LineNumber = lineNumber,
            CimsCostCodeId = cimsCostCodeId,
            Description = description,
            Quantity = quantity,
            UnitOfMeasure = unitOfMeasure,
            UnitRate = unitRate,
            Value = unitRate.Multiply(quantity),
        };
    }
}
