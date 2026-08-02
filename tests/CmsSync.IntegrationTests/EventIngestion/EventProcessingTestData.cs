using System.Text;
using System.Text.Json;
using CmsSync.Application.EventIngestion;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.EventIngestion;

internal static class EventProcessingTestData
{
    public const string DefaultTimestamp = "2026-08-02T10:00:00.0000000Z";

    public static string UniqueId(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    public static string Publish(
        string entityId,
        long version = 1,
        string? eventId = null,
        string timestamp = DefaultTimestamp,
        string payload = "{\"value\":1}",
        string type = "publish")
    {
        var eventIdProperty = eventId is null
            ? string.Empty
            : $"\"eventId\":{JsonSerializer.Serialize(eventId)},";

        return
            $"{{{eventIdProperty}\"type\":{JsonSerializer.Serialize(type)}," +
            $"\"id\":{JsonSerializer.Serialize(entityId)},\"version\":{version}," +
            $"\"timestamp\":{JsonSerializer.Serialize(timestamp)},\"payload\":{payload}}}";
    }

    public static string Delete(
        string entityId,
        string? eventId = null,
        string timestamp = DefaultTimestamp)
    {
        var eventIdProperty = eventId is null
            ? string.Empty
            : $"\"eventId\":{JsonSerializer.Serialize(eventId)},";

        return
            $"{{{eventIdProperty}\"type\":\"delete\",\"id\":{JsonSerializer.Serialize(entityId)}," +
            $"\"timestamp\":{JsonSerializer.Serialize(timestamp)}}}";
    }

    public static CmsEventBatchRequest CreateRequest(
        IReadOnlyList<string> eventJson,
        Guid? batchId = null,
        string? correlationId = null,
        string? authenticatedSubject = null)
    {
        var body = $"[{string.Join(',', eventJson)}]";
        var parsed = new CmsEventArrayParser().Parse(Encoding.UTF8.GetBytes(body));

        Assert.True(parsed.IsSuccess, parsed.Failure?.Code);
        return new CmsEventBatchRequest(
            batchId ?? Guid.NewGuid(),
            parsed.Items,
            correlationId ?? UniqueId("correlation"),
            authenticatedSubject ?? UniqueId("cms-subject"));
    }

    public static async Task<CmsEventBatchResult> ProcessAsync(
        IServiceProvider services,
        IReadOnlyList<string> eventJson,
        Guid? batchId = null,
        string? correlationId = null,
        string? authenticatedSubject = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var batchService = scope.ServiceProvider.GetRequiredService<CmsEventBatchService>();
        var request = CreateRequest(
            eventJson,
            batchId,
            correlationId,
            authenticatedSubject);

        return await batchService.ProcessAsync(request, cancellationToken);
    }
}
