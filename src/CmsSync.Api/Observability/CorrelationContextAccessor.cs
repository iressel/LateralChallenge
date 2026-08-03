namespace CmsSync.Api.Observability;

public static class CorrelationContextAccessor
{
    private static readonly object CorrelationIdentifierKey = new();
    private static readonly Func<ILogger, string, string, IDisposable?> CorrelationScope =
        LoggerMessage.DefineScope<string, string>(
            "CorrelationId {CorrelationId} TraceId {TraceId}");

    public static string GetCorrelationId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Items.TryGetValue(CorrelationIdentifierKey, out var value) &&
            value is string correlationId)
        {
            return correlationId;
        }

        throw new InvalidOperationException("The request correlation identifier has not been initialized.");
    }

    internal static void SetCorrelationId(HttpContext context, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        context.Items[CorrelationIdentifierKey] = correlationId;
    }

    internal static IDisposable? BeginLoggingScope(
        ILogger logger,
        string correlationId,
        string traceId)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return CorrelationScope(logger, correlationId, traceId);
    }
}
