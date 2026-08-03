using Microsoft.Extensions.Logging;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class CapturedLogger : ILogger
{
    private readonly string _category;
    private readonly CapturedLogProvider _provider;

    public CapturedLogger(string category, CapturedLogProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return _provider.PushScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _provider.Record(
            _category,
            logLevel,
            eventId,
            formatter(state, exception),
            state?.ToString() ?? string.Empty,
            exception?.ToString() ?? string.Empty);
    }
}
