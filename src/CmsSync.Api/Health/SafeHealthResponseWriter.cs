using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CmsSync.Api.Health;

internal static class SafeHealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json; charset=utf-8";
        var response = report.Status == HealthStatus.Healthy
            ? "{\"status\":\"Healthy\"}"
            : "{\"status\":\"Unhealthy\"}";
        return context.Response.WriteAsync(response, context.RequestAborted);
    }
}
