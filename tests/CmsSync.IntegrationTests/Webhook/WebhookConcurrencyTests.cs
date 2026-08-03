using System.Net;
using System.Text.Json;
using CmsSync.IntegrationTests.Infrastructure;
using Xunit;

namespace CmsSync.IntegrationTests.Webhook;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "Webhook")]
public sealed class WebhookConcurrencyTests
{
    private static readonly string?[] AppliedAndDuplicate = ["applied", "duplicate"];
    private static readonly string?[] AppliedAndConflict = ["applied", "conflict"];
    private static readonly string?[] AppliedAndStale = ["applied", "stale"];

    private readonly SqlServerFixture _fixture;

    public WebhookConcurrencyTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentExactRequestsProduceOneMutationAndOneDuplicate()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var secondClient = host.CreateIndependentClient();
        var entityId = WebhookTestData.UniqueId("concurrent-duplicate");
        var eventJson = WebhookTestData.Publish(
            entityId,
            eventId: WebhookTestData.UniqueId("concurrent-duplicate-event"));

        var responses = await PostPairAsync(
            host,
            host.Client,
            secondClient,
            eventJson,
            eventJson);
        using var firstResponse = responses.First;
        using var secondResponse = responses.Second;
        var outcomes = await ReadSortedOutcomesAsync(firstResponse, secondResponse);
        var logs = await WebhookDatabaseTestData.ReadLogsAsync(host, entityId);

        Assert.Equal(AppliedAndDuplicate, outcomes);
        Assert.Equal(2, logs.Length);
        Assert.Single(logs, log => log.OwnsIdempotencyKey);
        Assert.Single(logs, log => !log.OwnsIdempotencyKey && log.ReplayOfProcessingLogId is not null);
        Assert.Equal(1, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));
    }

    [Fact]
    public async Task ConcurrentCompetingPayloadsPreserveOneImmutableRevision()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var secondClient = host.CreateIndependentClient();
        var entityId = WebhookTestData.UniqueId("concurrent-conflict");
        var firstEvent = WebhookTestData.Publish(
            entityId,
            eventId: WebhookTestData.UniqueId("competing-event"),
            payload: "{\"winner\":1}");
        var secondEvent = WebhookTestData.Publish(
            entityId,
            eventId: WebhookTestData.UniqueId("competing-event"),
            payload: "{\"winner\":2}");

        var responses = await PostPairAsync(
            host,
            host.Client,
            secondClient,
            firstEvent,
            secondEvent);
        using var firstResponse = responses.First;
        using var secondResponse = responses.Second;
        var outcomes = await ReadSortedOutcomesAsync(firstResponse, secondResponse);
        var revision = await WebhookDatabaseTestData.ReadRevisionAsync(host, entityId, 1, 1);

        Assert.Equal(AppliedAndConflict, outcomes);
        Assert.True(
            revision!.FirstObservedPayload is "{\"winner\":1}" or "{\"winner\":2}");
        Assert.Equal(1, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));
    }

    [Fact]
    public async Task ConcurrentPublishAndDeleteRequestsObserveOneValidSerialOrder()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var secondClient = host.CreateIndependentClient();
        var entityId = WebhookTestData.UniqueId("concurrent-publish-delete");
        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Publish(entityId, timestamp: "2026-08-02T10:00:00Z"),
            "applied");
        var publish = WebhookTestData.Publish(
            entityId,
            version: 2,
            eventId: WebhookTestData.UniqueId("publish-delete-publish"),
            timestamp: "2026-08-02T11:00:00Z",
            payload: "{\"version\":2}");
        var delete = WebhookTestData.Delete(
            entityId,
            eventId: WebhookTestData.UniqueId("publish-delete-delete"),
            timestamp: "2026-08-02T10:30:00Z");

        var responses = await PostPairAsync(
            host,
            host.Client,
            secondClient,
            publish,
            delete);
        using var firstResponse = responses.First;
        using var secondResponse = responses.Second;
        var outcomes = await ReadOutcomesAsync(firstResponse, secondResponse);
        var entity = await WebhookDatabaseTestData.ReadEntityAsync(host, entityId);

        Assert.Equal("applied", outcomes[0]);
        Assert.True(outcomes[1] is "applied" or "stale");
        Assert.Equal(2, entity!.LatestVersion);
        Assert.Equal(new DateTime(2026, 8, 2, 11, 0, 0, DateTimeKind.Utc), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(entity.CurrentVersionOccurredAtUtc, entity.EntityEventHighWatermarkUtc);
        Assert.Equal(3, (await WebhookDatabaseTestData.ReadLogsAsync(host, entityId)).Length);
    }

    [Fact]
    public async Task AC053ConcurrentOlderHigherVersionAndDeletePreserveHighWatermark()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var secondClient = host.CreateIndependentClient();
        var entityId = WebhookTestData.UniqueId("ac053-http");
        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Publish(entityId, version: 5, timestamp: "2026-08-02T10:00:00Z"),
            "applied");
        var higherVersion = WebhookTestData.Publish(
            entityId,
            version: 6,
            eventId: WebhookTestData.UniqueId("ac053-version"),
            timestamp: "2026-08-02T09:00:00Z",
            payload: "{\"version\":6}");
        var staleDelete = WebhookTestData.Delete(
            entityId,
            eventId: WebhookTestData.UniqueId("ac053-delete"),
            timestamp: "2026-08-02T09:30:00Z");

        var responses = await PostPairAsync(
            host,
            host.Client,
            secondClient,
            higherVersion,
            staleDelete);
        using var firstResponse = responses.First;
        using var secondResponse = responses.Second;
        var outcomes = await ReadSortedOutcomesAsync(firstResponse, secondResponse);
        var entity = await WebhookDatabaseTestData.ReadEntityAsync(host, entityId);

        Assert.Equal(AppliedAndStale, outcomes);
        Assert.Equal(6, entity!.LatestVersion);
        Assert.Equal(new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc), entity.CurrentVersionOccurredAtUtc);
        Assert.Equal(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc), entity.EntityEventHighWatermarkUtc);
        Assert.Equal(2, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));
    }

    private static async Task<(HttpResponseMessage First, HttpResponseMessage Second)> PostPairAsync(
        WebhookTestHost host,
        HttpClient firstClient,
        HttpClient secondClient,
        string firstEvent,
        string secondEvent)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = PostAfterGateAsync(host, firstClient, firstEvent, gate.Task);
        var secondTask = PostAfterGateAsync(host, secondClient, secondEvent, gate.Task);
        gate.SetResult();

        var responses = await Task.WhenAll(firstTask, secondTask);
        return (responses[0], responses[1]);
    }

    private static async Task<HttpResponseMessage> PostAfterGateAsync(
        WebhookTestHost host,
        HttpClient client,
        string eventJson,
        Task gate)
    {
        await gate.WaitAsync(TestContext.Current.CancellationToken);
        return await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(eventJson),
            client: client);
    }

    private static async Task<string?[]> ReadSortedOutcomesAsync(
        HttpResponseMessage firstResponse,
        HttpResponseMessage secondResponse)
    {
        var outcomes = await ReadOutcomesAsync(firstResponse, secondResponse);
        Array.Sort(outcomes, StringComparer.Ordinal);
        return outcomes;
    }

    private static async Task<string?[]> ReadOutcomesAsync(
        HttpResponseMessage firstResponse,
        HttpResponseMessage secondResponse)
    {
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var firstJson = await WebhookTestData.ReadJsonAsync(firstResponse);
        using var secondJson = await WebhookTestData.ReadJsonAsync(secondResponse);
        return
        [
            ReadOutcome(firstJson.RootElement),
            ReadOutcome(secondJson.RootElement),
        ];
    }

    private static string? ReadOutcome(JsonElement root)
    {
        return root.GetProperty("results")[0].GetProperty("outcome").GetString();
    }

    private static async Task PostAndAssertOutcomeAsync(
        WebhookTestHost host,
        string eventJson,
        string expectedOutcome)
    {
        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(eventJson));
        using var json = await WebhookTestData.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedOutcome, ReadOutcome(json.RootElement));
    }
}
