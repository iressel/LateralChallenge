using System.Diagnostics.Metrics;
using CmsSync.Domain.Processing;

namespace CmsSync.Application.Observability;

public static class CmsOperationalMetrics
{
    public const string MeterName = "CmsSync.Operations";
    public const string BatchCountInstrument = "cms.ingestion.batches";
    public const string EventCountInstrument = "cms.ingestion.events";
    public const string BatchLatencyInstrument = "cms.ingestion.batch.duration";
    public const string EventLatencyInstrument = "cms.ingestion.event.duration";
    public const string OutcomeCodeInstrument = "cms.ingestion.outcome_codes";
    public const string SqlTransientRetryInstrument = "cms.sql.transient_retries";
    public const string SqlDeadlockInstrument = "cms.sql.deadlocks";
    public const string SqlFailureInstrument = "cms.sql.failures";
    public const string AuthenticationFailureInstrument = "cms.authentication.failures";

    public const string WriteDatabaseOperation = "write_database";
    public const string ReadDatabaseOperation = "read_database";
    public const string ReadinessWriteOperation = "readiness_write";
    public const string ReadinessReadOperation = "readiness_read";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> BatchCounter = Meter.CreateCounter<long>(BatchCountInstrument);
    private static readonly Counter<long> EventCounter = Meter.CreateCounter<long>(EventCountInstrument);
    private static readonly Histogram<double> BatchLatency = Meter.CreateHistogram<double>(
        BatchLatencyInstrument,
        unit: "ms");
    private static readonly Histogram<double> EventLatency = Meter.CreateHistogram<double>(
        EventLatencyInstrument,
        unit: "ms");
    private static readonly Counter<long> OutcomeCodeCounter = Meter.CreateCounter<long>(OutcomeCodeInstrument);
    private static readonly Counter<long> SqlTransientRetryCounter = Meter.CreateCounter<long>(
        SqlTransientRetryInstrument);
    private static readonly Counter<long> SqlDeadlockCounter = Meter.CreateCounter<long>(SqlDeadlockInstrument);
    private static readonly Counter<long> SqlFailureCounter = Meter.CreateCounter<long>(SqlFailureInstrument);
    private static readonly Counter<long> AuthenticationFailureCounter = Meter.CreateCounter<long>(
        AuthenticationFailureInstrument);

    public static void RecordBatchStarted()
    {
        BatchCounter.Add(1);
    }

    public static void RecordBatchLatency(double elapsedMilliseconds, string resultClass)
    {
        BatchLatency.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("result_class", resultClass));
    }

    public static void RecordEvent(
        ProcessingOutcome outcome,
        string code,
        double elapsedMilliseconds)
    {
        var outcomeName = GetOutcomeName(outcome);
        EventCounter.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcomeName));
        EventLatency.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("result_class", "deterministic"),
            new KeyValuePair<string, object?>("outcome", outcomeName));

        if (outcome is ProcessingOutcome.Invalid or ProcessingOutcome.Conflict)
        {
            OutcomeCodeCounter.Add(
                1,
                new KeyValuePair<string, object?>("outcome", outcomeName),
                new KeyValuePair<string, object?>("code", code));
        }
    }

    public static void RecordEventFailure(double elapsedMilliseconds, string resultClass)
    {
        EventLatency.Record(
            elapsedMilliseconds,
            new KeyValuePair<string, object?>("result_class", resultClass));
    }

    public static void RecordSqlFailure(
        string operation,
        string resultClass,
        bool isTransient,
        bool isDeadlock)
    {
        SqlFailureCounter.Add(
            1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result_class", resultClass));

        if (isTransient)
        {
            SqlTransientRetryCounter.Add(
                1,
                new KeyValuePair<string, object?>("operation", operation));
        }

        if (isDeadlock)
        {
            SqlDeadlockCounter.Add(
                1,
                new KeyValuePair<string, object?>("operation", operation));
        }
    }

    public static void RecordAuthenticationFailure(string scheme, string resultClass)
    {
        AuthenticationFailureCounter.Add(
            1,
            new KeyValuePair<string, object?>("scheme", scheme),
            new KeyValuePair<string, object?>("result_class", resultClass));
    }

    private static string GetOutcomeName(ProcessingOutcome outcome)
    {
        return outcome switch
        {
            ProcessingOutcome.Applied => "applied",
            ProcessingOutcome.Duplicate => "duplicate",
            ProcessingOutcome.Equivalent => "equivalent",
            ProcessingOutcome.Stale => "stale",
            ProcessingOutcome.Invalid => "invalid",
            ProcessingOutcome.Conflict => "conflict",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "The outcome is not supported."),
        };
    }
}
