using Financials.Application.Common.Authorization;
using Financials.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Financials.Application.Common.Behaviours;

/// <summary>
/// MediatR pipeline behaviour that enforces <see cref="RequiresPermissionAttribute"/>.
/// If the request type carries the attribute, the user must have the named
/// permission claim or the handler is never invoked — the behaviour returns
/// a failed <see cref="Result"/> (or <see cref="Result{T}"/>) with
/// <see cref="FailureReason.Unauthorized"/>.
///
/// Runs before validation so we don't reveal which fields are wrong to an
/// unauthorised caller. See ADR-0010 (FailureReason) and the M-2 finding.
/// </summary>
public sealed partial class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IPermissionService _permissions;
    private readonly ILogger<AuthorizationBehaviour<TRequest, TResponse>> _logger;

    public AuthorizationBehaviour(
        IPermissionService permissions,
        ILogger<AuthorizationBehaviour<TRequest, TResponse>> logger)
    {
        _permissions = permissions;
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var attribute = typeof(TRequest).GetCustomAttributes(typeof(RequiresPermissionAttribute), inherit: true)
            .OfType<RequiresPermissionAttribute>()
            .FirstOrDefault();

        if (attribute is not null && !_permissions.Has(attribute.Permission))
        {
            LogUnauthorized(_logger, typeof(TRequest).Name, attribute.Permission);
            return (TResponse)BuildUnauthorizedResult(typeof(TResponse), attribute.Permission);
        }

        return await next().ConfigureAwait(false);
    }

    /// <summary>
    /// Build a typed failed Result for the response type without taking a
    /// runtime dependency on every TResponse. Handlers return either
    /// <see cref="Result"/> or <see cref="Result{T}"/>; this dispatcher
    /// covers both via reflection. The response type is fixed at class-
    /// closure time so the reflection cost is amortised across requests.
    /// </summary>
    private static object BuildUnauthorizedResult(Type responseType, string permission)
    {
        var detail = $"Caller lacks the '{permission}' permission.";

        if (responseType == typeof(Result))
        {
            return Result.Unauthorized(detail);
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            // Result<T>.Unauthorized is a static method; invoke via reflection.
            var method = responseType.GetMethod(
                nameof(Result<object>.Unauthorized),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null)
                ?? throw new InvalidOperationException(
                    $"Result<{responseType.GetGenericArguments()[0].Name}> is missing a static Unauthorized(string) factory.");
            return method.Invoke(null, new object[] { detail })!;
        }

        throw new InvalidOperationException(
            $"AuthorizationBehaviour can only enforce on requests whose response is Result or Result<T>; "
            + $"got {responseType.FullName}. Either add the response shape or remove [RequiresPermission].");
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Request {RequestName} denied: caller lacks permission '{Permission}'")]
    private static partial void LogUnauthorized(ILogger logger, string requestName, string permission);
}
