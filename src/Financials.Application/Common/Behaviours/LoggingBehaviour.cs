using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Financials.Application.Common.Behaviours;

/// <summary>
/// MediatR pipeline behaviour that emits structured start/end log entries
/// per request, with elapsed milliseconds and the resolved request type.
/// Serilog request enrichment (correlation id, user id) is applied at the
/// HTTP middleware layer; this behaviour adds the per-handler timing only.
/// </summary>
public sealed partial class LoggingBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehaviour<TRequest, TResponse>> _logger;

    public LoggingBehaviour(ILogger<LoggingBehaviour<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;
        LogRequestStart(_logger, requestName);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next().ConfigureAwait(false);
            stopwatch.Stop();
            LogRequestEnd(_logger, requestName, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogRequestFailed(_logger, ex, requestName, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Handling {RequestName}")]
    private static partial void LogRequestStart(ILogger logger, string requestName);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Handled {RequestName} in {ElapsedMs} ms")]
    private static partial void LogRequestEnd(ILogger logger, string requestName, long elapsedMs);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "Handling {RequestName} failed after {ElapsedMs} ms")]
    private static partial void LogRequestFailed(ILogger logger, Exception exception, string requestName, long elapsedMs);
}
