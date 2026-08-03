using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class CapturedLogProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public IReadOnlyCollection<CapturedLogEntry> Entries
    {
        get
        {
            return _entries.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturedLogger(categoryName, this);
    }

    public void Dispose()
    {
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    public bool ContainsAny(IEnumerable<string> sensitiveValues)
    {
        var values = sensitiveValues
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in _entries)
        {
            if (ContainsValue(entry.Category, values) ||
                ContainsValue(entry.Message, values) ||
                ContainsValue(entry.State, values) ||
                ContainsValue(entry.Exception, values) ||
                entry.Scopes.Any(scope => ContainsValue(scope, values)))
            {
                return true;
            }
        }

        return false;
    }

    internal IDisposable PushScope<TState>(TState state)
        where TState : notnull
    {
        return _scopeProvider.Push(state);
    }

    internal void Record(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        string state,
        string exception)
    {
        var scopes = new List<string>();
        _scopeProvider.ForEachScope(
            (scope, collectedScopes) => collectedScopes.Add(scope?.ToString() ?? string.Empty),
            scopes);
        _entries.Enqueue(new CapturedLogEntry(
            category,
            level,
            eventId,
            message,
            state,
            exception,
            scopes));
    }

    private static bool ContainsValue(string candidate, IReadOnlyCollection<string> values)
    {
        return values.Any(value => candidate.Contains(value, StringComparison.Ordinal));
    }
}
