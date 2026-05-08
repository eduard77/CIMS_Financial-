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
            throw new ArgumentException("A CIMS cost code is required.", nameof(cimsCostCodeId));
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is required.", nameof(description));
        }
        if (string.IsNullOrWhiteSpace(unitOfMeasure))
        {
            throw new ArgumentException("Unit of measure is required.", nameof(unitOfMeasure));
        }
        if (lineNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber), lineNumber, "Line number must be positive.");
        }
        if (quantity < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be non-negative.");
        }
        ArgumentNullException.ThrowIfNull(unitRate);

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
