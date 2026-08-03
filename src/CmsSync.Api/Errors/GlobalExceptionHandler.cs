using CmsSync.Api.Entities;
using CmsSync.Api.Webhook;
using CmsSync.Application.AdministrativeState;
using CmsSync.Application.EventIngestion;
using Microsoft.AspNetCore.Diagnostics;

namespace CmsSync.Api.Errors;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private static readonly Action<ILogger, int, string, string, Exception?> FailureHandled =
        LoggerMessage.Define<int, string, string>(
            LogLevel.Error,
            new EventId(1404, nameof(FailureHandled)),
            "Unhandled request failure converted to Problem Details. StatusCode {StatusCode} " +
            "ResultClass {ResultClass} RoutePattern {RoutePattern}");

    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var (statusCode, title, detail, code, resultClass) = Classify(httpContext, exception);
        FailureHandled(
            _logger,
            statusCode,
            resultClass,
            ReadRoutePattern(httpContext),
            null);
        await SafeProblemDetails.WriteAsync(httpContext, statusCode, title, detail, code);
        return true;
    }

    private static (int StatusCode, string Title, string Detail, string Code, string ResultClass) Classify(
        HttpContext context,
        Exception exception)
    {
        if (exception is EventProcessingDependencyUnavailableException)
        {
            return (
                StatusCodes.Status503ServiceUnavailable,
                "CMS event processing is temporarily unavailable",
                "A required dependency could not complete the CMS event batch.",
                CmsWebhookProblemCodes.DependencyUnavailable,
                "dependency_unavailable");
        }

        if (exception is AdministrativeStateDependencyUnavailableException)
        {
            return (
                StatusCodes.Status503ServiceUnavailable,
                "Administrative state unavailable",
                "The administrative state could not be updated at this time.",
                CmsEntityProblemCodes.AdministrativeStateUnavailable,
                "dependency_unavailable");
        }

        if (context.Request.Path.StartsWithSegments(CmsEventsEndpoint.Route))
        {
            return (
                StatusCodes.Status500InternalServerError,
                "CMS event processing failed",
                "The CMS event batch could not be completed.",
                CmsWebhookProblemCodes.UnexpectedProcessingFailure,
                "unexpected_failure");
        }

        if (context.Request.Path.StartsWithSegments(CmsEntitiesEndpoint.RoutePrefix))
        {
            if (context.Request.Path.Value?.EndsWith(
                    "/administrative-state",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return (
                    StatusCodes.Status500InternalServerError,
                    "Administrative state update failed",
                    "The administrative state could not be updated.",
                    CmsEntityProblemCodes.AdministrativeStateUpdateFailed,
                    "unexpected_failure");
            }

            return (
                StatusCodes.Status500InternalServerError,
                "Entity query failed",
                "The requested entity data could not be retrieved.",
                CmsEntityProblemCodes.QueryFailed,
                "unexpected_failure");
        }

        return (
            StatusCodes.Status500InternalServerError,
            "Unexpected server failure",
            "The request could not be completed.",
            GlobalProblemCodes.UnexpectedServerFailure,
            "unexpected_failure");
    }

    private static string ReadRoutePattern(HttpContext context)
    {
        return context.GetEndpoint() is Microsoft.AspNetCore.Routing.RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? "matched"
            : "unmatched";
    }
}
