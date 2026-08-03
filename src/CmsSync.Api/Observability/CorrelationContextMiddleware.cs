using System.Diagnostics;
using Microsoft.Extensions.Primitives;

namespace CmsSync.Api.Observability;

public sealed class CorrelationContextMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private const int MaximumCorrelationIdentifierLength = 64;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationContextMiddleware> _logger;

    public CorrelationContextMiddleware(
        RequestDelegate next,
        ILogger<CorrelationContextMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = ReadOrCreateCorrelationIdentifier(context.Request.Headers[HeaderName]);
        var traceId = Activity.Current?.TraceId.ToString() ?? "none";
        CorrelationContextAccessor.SetCorrelationId(context, correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var scope = CorrelationContextAccessor.BeginLoggingScope(_logger, correlationId, traceId);
        await _next(context);
    }

    private static string ReadOrCreateCorrelationIdentifier(StringValues suppliedValues)
    {
        if (suppliedValues.Count == 1 && IsSafeCorrelationIdentifier(suppliedValues[0]))
        {
            return suppliedValues[0]!;
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsSafeCorrelationIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumCorrelationIdentifierLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')
            {
                return false;
            }
        }

        return true;
    }
}
