using Microsoft.Extensions.Logging;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class CapturedLogEntry
{
    public CapturedLogEntry(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        string state,
        string exception,
        IReadOnlyList<string> scopes)
    {
        Category = category;
        Level = level;
        EventId = eventId;
        Message = message;
        State = state;
        Exception = exception;
        Scopes = scopes;
    }

    public string Category { get; }

    public LogLevel Level { get; }

    public EventId EventId { get; }

    public string Message { get; }

    public string State { get; }

    public string Exception { get; }

    public IReadOnlyList<string> Scopes { get; }
}
