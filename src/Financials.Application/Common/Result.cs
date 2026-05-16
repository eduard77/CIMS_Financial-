using System.Diagnostics.CodeAnalysis;
using Financials.Domain.Common;

namespace Financials.Application.Common;

/// <summary>
/// Lightweight Result type for command/query handlers (CLAUDE.md §7).
/// Hand-rolled to avoid a third-party dependency for a four-line concept.
///
/// A failed Result carries a <see cref="FailureReason"/> (defaults to
/// <see cref="FailureReason.Unspecified"/> for the legacy single-string
/// overload). See ADR-0010 (<c>docs/decisions/0010-failure-vs-exception.md</c>).
/// </summary>
public class Result
{
    public bool IsSuccess { get; }

    public string? Error { get; }

    public FailureReason Reason { get; }

    public IReadOnlyList<string>? ValidationErrors { get; }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    protected Result(bool isSuccess, FailureReason reason, string? error, IReadOnlyList<string>? validationErrors)
    {
        IsSuccess = isSuccess;
        Reason = reason;
        Error = error;
        ValidationErrors = validationErrors;
    }

    public static Result Success() => new(true, FailureReason.Unspecified, null, null);

    public static Result Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result(false, FailureReason.Unspecified, error, null);
    }

    public static Result Failure(FailureReason reason, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result(false, reason, error, null);
    }

    public static Result NotFound(string detail) => Failure(FailureReason.NotFound, detail);

    public static Result Conflict(string detail) => Failure(FailureReason.Conflict, detail);

    public static Result PreconditionFailed(string detail) => Failure(FailureReason.PreconditionFailed, detail);

    public static Result Unauthorized(string detail) => Failure(FailureReason.Unauthorized, detail);

    public static Result DependencyUnavailable(string detail) => Failure(FailureReason.DependencyUnavailable, detail);

    public static Result ValidationFailure(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException(
                "ValidationFailure requires at least one error message; got empty sequence.",
                nameof(errors));
        }
        return new Result(false, FailureReason.ValidationFailed, "Validation failed.", list);
    }
}

[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Static factory methods on Result<T> are the idiomatic API for the Result pattern and are widely used in callers.")]
public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool isSuccess, FailureReason reason, T? value, string? error, IReadOnlyList<string>? validationErrors)
        : base(isSuccess, reason, error, validationErrors)
    {
        Value = value;
    }

    public static Result<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Result<T>(true, FailureReason.Unspecified, value, null, null);
    }

    public static new Result<T> Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result<T>(false, FailureReason.Unspecified, default, error, null);
    }

    public static new Result<T> Failure(FailureReason reason, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new Result<T>(false, reason, default, error, null);
    }

    public static new Result<T> NotFound(string detail) => Failure(FailureReason.NotFound, detail);

    public static new Result<T> Conflict(string detail) => Failure(FailureReason.Conflict, detail);

    public static new Result<T> PreconditionFailed(string detail) => Failure(FailureReason.PreconditionFailed, detail);

    public static new Result<T> Unauthorized(string detail) => Failure(FailureReason.Unauthorized, detail);

    public static new Result<T> DependencyUnavailable(string detail) => Failure(FailureReason.DependencyUnavailable, detail);

    public static new Result<T> ValidationFailure(IEnumerable<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var list = errors.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException(
                "ValidationFailure requires at least one error message; got empty sequence.",
                nameof(errors));
        }
        return new Result<T>(false, FailureReason.ValidationFailed, default, "Validation failed.", list);
    }
}
