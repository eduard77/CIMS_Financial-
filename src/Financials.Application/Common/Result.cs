using System.Diagnostics.CodeAnalysis;

namespace Financials.Application.Common;

/// <summary>
/// Lightweight Result type for command/query handlers (CLAUDE.md §7).
/// Hand-rolled to avoid a third-party dependency for a four-line concept.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }

    public string? Error { get; }

    public IReadOnlyList<string>? ValidationErrors { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    protected Result(bool isSuccess, string? error, IReadOnlyList<string>? validationErrors)
    {
        IsSuccess = isSuccess;
        Error = error;
        ValidationErrors = validationErrors;
    }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result(false, error, null);
    }

    public static Result ValidationFailure(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.ToList();
        return new Result(false, "Validation failed.", list);
    }
}

[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Static factory methods on Result<T> are the idiomatic API for the Result pattern and are widely used in callers.")]
public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, T? value, string? error, IReadOnlyList<string>? validationErrors)
        : base(isSuccess, error, validationErrors)
    {
        Value = value;
    }

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(true, value, null, null);
    }

    public static new Result<T> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result<T>(false, default, error, null);
    }

    public static new Result<T> ValidationFailure(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.ToList();
        return new Result<T>(false, default, "Validation failed.", list);
    }
}
