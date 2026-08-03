using CmsSync.Application.Observability;

namespace CmsSync.Api.Observability;

public sealed class CmsEventBatchTelemetry
{
    private static readonly Action<ILogger, Guid, int, string, string, Exception?> BatchStarted =
        LoggerMessage.Define<Guid, int, string, string>(
            LogLevel.Information,
            new EventId(1405, nameof(BatchStarted)),
            "CMS event batch started. BatchId {BatchId} EventCount {EventCount} " +
            "CorrelationId {CorrelationId} TraceId {TraceId}");
    private static readonly Action<ILogger, Guid, int, double, string, string, Exception?> BatchCompleted =
        LoggerMessage.Define<Guid, int, double, string, string>(
            LogLevel.Information,
            new EventId(1406, nameof(BatchCompleted)),
            "CMS event batch completed. BatchId {BatchId} EventCount {EventCount} " +
            "ElapsedMilliseconds {ElapsedMilliseconds} CorrelationId {CorrelationId} TraceId {TraceId}");
    private static readonly Action<ILogger, Guid, int, string, double, string, Exception?> BatchFailed =
        LoggerMessage.Define<Guid, int, string, double, string>(
            LogLevel.Error,
            new EventId(1407, nameof(BatchFailed)),
            "CMS event batch failed. BatchId {BatchId} EventCount {EventCount} " +
            "ResultClass {ResultClass} ElapsedMilliseconds {ElapsedMilliseconds} " +
            "CorrelationId {CorrelationId}");

    private readonly ILogger<CmsEventBatchTelemetry> _logger;

    public CmsEventBatchTelemetry(ILogger<CmsEventBatchTelemetry> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RecordStarted(
        Guid batchId,
        int eventCount,
        string correlationId,
        string traceId)
    {
        CmsOperationalMetrics.RecordBatchStarted();
        BatchStarted(_logger, batchId, eventCount, correlationId, traceId, null);
    }

    public void RecordCompleted(
        Guid batchId,
        int eventCount,
        double elapsedMilliseconds,
        string correlationId,
        string traceId)
    {
        CmsOperationalMetrics.RecordBatchLatency(elapsedMilliseconds, "completed");
        BatchCompleted(
            _logger,
            batchId,
            eventCount,
            elapsedMilliseconds,
            correlationId,
            traceId,
            null);
    }

    public void RecordFailed(
        Guid batchId,
        int eventCount,
        string resultClass,
        double elapsedMilliseconds,
        string correlationId)
    {
        CmsOperationalMetrics.RecordBatchLatency(elapsedMilliseconds, resultClass);
        BatchFailed(
            _logger,
            batchId,
            eventCount,
            resultClass,
            elapsedMilliseconds,
            correlationId,
            null);
    }
}
