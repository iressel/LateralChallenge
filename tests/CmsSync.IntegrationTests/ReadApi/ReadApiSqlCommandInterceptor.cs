using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CmsSync.IntegrationTests.ReadApi;

internal sealed class ReadApiSqlCommandInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<string> _commandTexts = new();

    public IReadOnlyList<string> CommandTexts => _commandTexts.ToArray();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commandTexts.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
