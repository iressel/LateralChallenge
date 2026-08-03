using System.Diagnostics;
using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Routing;

namespace CmsSync.Api.Observability;

public sealed class SafeRequestLoggingMiddleware
{
    private static readonly Action<ILogger, string, string, int, double, string, string, Exception?> RequestCompleted =
        LoggerMessage.Define<string, string, int, double, string, string>(
            LogLevel.Information,
            new EventId(1403, nameof(RequestCompleted)),
            "HTTP request completed. Method {Method} RoutePattern {RoutePattern} StatusCode {StatusCode} " +
            "ElapsedMilliseconds {ElapsedMilliseconds} AuthenticationScheme {AuthenticationScheme} " +
            "AuthenticatedRole {AuthenticatedRole}");

    private readonly RequestDelegate _next;
    private readonly ILogger<SafeRequestLoggingMiddleware> _logger;

    public SafeRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<SafeRequestLoggingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var startedTimestamp = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        finally
        {
            RequestCompleted(
                _logger,
                context.Request.Method,
                ReadRoutePattern(context),
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                ReadAuthenticationScheme(context),
                ReadAuthenticatedRole(context),
                null);
        }
    }

    private static string ReadRoutePattern(HttpContext context)
    {
        return context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText ?? "matched"
            : "unmatched";
    }

    private static string ReadAuthenticationScheme(HttpContext context)
    {
        return context.User.Identity?.AuthenticationType switch
        {
            AuthenticationConstants.CmsScheme => AuthenticationConstants.CmsScheme,
            AuthenticationConstants.ConsumerScheme => AuthenticationConstants.ConsumerScheme,
            null => "none",
            _ => "other",
        };
    }

    private static string ReadAuthenticatedRole(HttpContext context)
    {
        if (context.User.IsInRole(AuthenticationConstants.CmsServiceRole))
        {
            return AuthenticationConstants.CmsServiceRole;
        }

        if (context.User.IsInRole(AuthenticationConstants.NormalConsumerRole))
        {
            return AuthenticationConstants.NormalConsumerRole;
        }

        if (context.User.IsInRole(AuthenticationConstants.AdministratorRole))
        {
            return AuthenticationConstants.AdministratorRole;
        }

        return context.User.Identity?.IsAuthenticated == true ? "other" : "none";
    }
}
