using System.Net;
using System.Text.Json;
using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using CmsSync.IntegrationTests.EventIngestion;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CmsSync.IntegrationTests.Webhook;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "Webhook")]
public sealed class WebhookProcessingTests
{
    private static readonly string?[] MixedOutcomes =
        ["applied", "duplicate", "equivalent", "stale", "invalid", "conflict"];
    private static readonly int[] FirstTwoSequences = [0, 1];
    private static readonly string?[] RetryOutcomes = ["duplicate", "applied"];

    private readonly SqlServerFixture _fixture;

    public WebhookProcessingTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MixedSixOutcomeBatchReturns200WithExactOrderedSummary()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("six-outcomes");
        var appliedEventId = WebhookTestData.UniqueId("applied-event");
        var applied = WebhookTestData.Publish(
            entityId,
            version: 2,
            eventId: appliedEventId,
            payload: "{\"value\":2}");
        var duplicate = applied;
        var equivalent = WebhookTestData.Publish(
            entityId,
            version: 2,
            eventId: WebhookTestData.UniqueId("equivalent-event"),
            payload: "{\"value\":2}");
        var stale = WebhookTestData.Publish(
            entityId,
            version: 1,
            eventId: WebhookTestData.UniqueId("stale-event"),
            timestamp: "2026-08-02T11:00:00Z",
            payload: "{\"value\":1}");
        var invalid = WebhookTestData.Publish(
            WebhookTestData.UniqueId("invalid-event"),
            type: "unsupported");
        var conflict = WebhookTestData.Publish(
            entityId,
            version: 2,
            eventId: WebhookTestData.UniqueId("conflict-event"),
            payload: "{\"value\":999}");

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(applied, duplicate, equivalent, stale, invalid, conflict));
        using var json = await WebhookTestData.ReadJsonAsync(response);
        var root = json.RootElement;
        var results = root.GetProperty("results");
        var summary = root.GetProperty("summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(Guid.Empty, root.GetProperty("batchId").GetGuid());
        Assert.Equal(Enumerable.Range(0, 6), results.EnumerateArray().Select(ReadSequence));
        Assert.Equal(
            MixedOutcomes,
            results.EnumerateArray().Select(ReadOutcome));
        Assert.Equal(7, summary.EnumerateObject().Count());
        Assert.Equal(6, summary.GetProperty("total").GetInt32());
        Assert.Equal(1, summary.GetProperty("applied").GetInt32());
        Assert.Equal(1, summary.GetProperty("duplicate").GetInt32());
        Assert.Equal(1, summary.GetProperty("equivalent").GetInt32());
        Assert.Equal(1, summary.GetProperty("stale").GetInt32());
        Assert.Equal(1, summary.GetProperty("invalid").GetInt32());
        Assert.Equal(1, summary.GetProperty("conflict").GetInt32());
        Assert.All(results.EnumerateArray(), item => Assert.False(item.TryGetProperty("payload", out _)));
    }

    [Fact]
    public async Task RecognizedDependencyFailureReturnsSafe503ProblemDetails()
    {
        var capturedLogs = new CapturedLogProvider();
        await using var host = WebhookTestHost.Create(
            _fixture,
            services => ReplaceExecutor(
                services,
                new ThrowingEventTransactionExecutor(
                    static () => new EventProcessingDependencyUnavailableException())),
            capturedLogs);
        var payloadSentinel = $"dependency-payload-{Guid.NewGuid():N}";
        var body = WebhookTestData.Array(
            WebhookTestData.Publish(
                WebhookTestData.UniqueId("dependency-failure"),
                payload: JsonSerializer.SerializeToElement(new { value = payloadSentinel }).GetRawText()));

        using var response = await WebhookTestData.PostCmsAsync(host, body);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("DEPENDENCY_UNAVAILABLE", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(payloadSentinel, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Credentials.CmsPassword, responseBody, StringComparison.Ordinal);
        Assert.False(capturedLogs.ContainsAny([payloadSentinel, host.Credentials.CmsPassword]));
    }

    [Fact]
    public async Task UnexpectedProcessingFailureReturnsSafe500WithoutExceptionOrPayloadDetails()
    {
        const string internalFailure = "SQL stack and connection detail must stay private";
        var capturedLogs = new CapturedLogProvider();
        await using var host = WebhookTestHost.Create(
            _fixture,
            services => ReplaceExecutor(
                services,
                new ThrowingEventTransactionExecutor(
                    static () => new InvalidOperationException(internalFailure))),
            capturedLogs);
        var payloadSentinel = $"unexpected-payload-{Guid.NewGuid():N}";
        var body = WebhookTestData.Array(
            WebhookTestData.Publish(
                WebhookTestData.UniqueId("unexpected-failure"),
                payload: $"{{\"secret\":\"{payloadSentinel}\"}}"));

        using var response = await WebhookTestData.PostCmsAsync(host, body);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("UNEXPECTED_PROCESSING_FAILURE", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(internalFailure, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(payloadSentinel, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.False(capturedLogs.ContainsAny(
            [internalFailure, payloadSentinel, host.Credentials.CmsPassword]));
    }

    [Fact]
    public async Task ResponseApplicationLogsAndProcessingLogsContainNoPayloadOrCredentials()
    {
        var capturedLogs = new CapturedLogProvider();
        await using var host = WebhookTestHost.Create(_fixture, capturedLogs: capturedLogs);
        var entityId = WebhookTestData.UniqueId("safe-logging");
        var eventId = WebhookTestData.UniqueId("safe-event");
        var payloadSentinel = $"payload-secret-{Guid.NewGuid():N}";
        var payload = $"{{\"secret\":\"{payloadSentinel}\"}}";

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(WebhookTestData.Publish(entityId, eventId: eventId, payload: payload)));
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var responseJson = JsonDocument.Parse(responseBody);
        var logs = await WebhookDatabaseTestData.ReadLogsAsync(host, entityId);
        var log = Assert.Single(logs);
        var authorizationParameter = AuthenticationRequestFactory.CreateBasicParameter(
            host.Credentials.CmsUsername,
            host.Credentials.CmsPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(payloadSentinel, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Credentials.CmsPassword, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain(payloadSentinel, string.Join('|', ReadSafeLogText(log)), StringComparison.Ordinal);
        Assert.Equal(responseJson.RootElement.GetProperty("batchId").GetGuid(), log.BatchId);
        Assert.Equal(host.Credentials.CmsUsername, log.AuthenticatedCmsSubject);
        Assert.False(string.IsNullOrWhiteSpace(log.CorrelationId));
        Assert.False(capturedLogs.ContainsAny(
            [payloadSentinel, host.Credentials.CmsPassword, authorizationParameter]));
    }

    [Fact]
    public async Task EarlierCommitSurvivesLaterFailureAndWholeRequestRetryIsSafe()
    {
        var firstEntityId = WebhookTestData.UniqueId("partial-first");
        var secondEntityId = WebhookTestData.UniqueId("partial-second");
        var firstEvent = WebhookTestData.Publish(
            firstEntityId,
            eventId: WebhookTestData.UniqueId("partial-event"));
        var secondEvent = WebhookTestData.Publish(
            secondEntityId,
            eventId: WebhookTestData.UniqueId("partial-event"));
        var body = WebhookTestData.Array(firstEvent, secondEvent);
        var productionExecutor = EventProcessingExecutorFactory.Create(_fixture.WriteConnectionString);
        var failingExecutor = new FailOnSequenceEventTransactionExecutor(productionExecutor, failingSequence: 1);

        await using (var failingHost = WebhookTestHost.Create(
            _fixture,
            services => ReplaceExecutor(services, failingExecutor)))
        {
            using var failedResponse = await WebhookTestData.PostCmsAsync(failingHost, body);

            Assert.Equal(HttpStatusCode.InternalServerError, failedResponse.StatusCode);
            Assert.NotNull(await WebhookDatabaseTestData.ReadEntityAsync(failingHost, firstEntityId));
            Assert.Null(await WebhookDatabaseTestData.ReadEntityAsync(failingHost, secondEntityId));
            Assert.Equal(FirstTwoSequences, failingExecutor.InvokedSequences);
        }

        await using var retryHost = WebhookTestHost.Create(_fixture);
        using var retryResponse = await WebhookTestData.PostCmsAsync(retryHost, body);
        using var retryJson = await WebhookTestData.ReadJsonAsync(retryResponse);

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(
            RetryOutcomes,
            retryJson.RootElement.GetProperty("results").EnumerateArray().Select(ReadOutcome));
        Assert.NotNull(await WebhookDatabaseTestData.ReadEntityAsync(retryHost, firstEntityId));
        Assert.NotNull(await WebhookDatabaseTestData.ReadEntityAsync(retryHost, secondEntityId));
    }

    [Fact]
    public async Task ExternalAndDerivedExactReplaysAreDuplicateThroughHttp()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var externalEntity = WebhookTestData.UniqueId("external-replay");
        var derivedEntity = WebhookTestData.UniqueId("derived-replay");
        var externalEvent = WebhookTestData.Publish(
            externalEntity,
            eventId: WebhookTestData.UniqueId("external-event"));
        var derivedEvent = WebhookTestData.Publish(derivedEntity);

        await AssertReplayAsync(host, externalEvent);
        await AssertReplayAsync(host, derivedEvent);

        Assert.Equal(2, (await WebhookDatabaseTestData.ReadLogsAsync(host, externalEntity)).Length);
        Assert.Equal(2, (await WebhookDatabaseTestData.ReadLogsAsync(host, derivedEntity)).Length);
    }

    [Fact]
    public async Task ExternalEventIdReuseWithDifferentContentConflictsWithoutMutation()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("event-id-conflict");
        var eventId = WebhookTestData.UniqueId("shared-event-id");

        using var firstResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(WebhookTestData.Publish(entityId, eventId: eventId, payload: "{\"value\":1}")));
        using var conflictResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(WebhookTestData.Publish(entityId, eventId: eventId, payload: "{\"value\":2}")));
        using var conflictJson = await WebhookTestData.ReadJsonAsync(conflictResponse);
        var revision = await WebhookDatabaseTestData.ReadRevisionAsync(host, entityId, 1, 1);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conflictResponse.StatusCode);
        Assert.Equal("conflict", conflictJson.RootElement.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("EVENT_ID_CONTENT_CONFLICT", conflictJson.RootElement.GetProperty("results")[0].GetProperty("code").GetString());
        Assert.Equal("{\"value\":1}", revision!.FirstObservedPayload);
    }

    [Fact]
    public async Task FailureBeforeCommitLeavesNoEntityRevisionOrProcessingLog()
    {
        var entityId = WebhookTestData.UniqueId("atomic-rollback");
        var interceptor = new TerminalBeforeCommitInterceptor();
        var executor = EventProcessingExecutorFactory.Create(
            _fixture.WriteConnectionString,
            [interceptor]);
        await using var host = WebhookTestHost.Create(
            _fixture,
            services => ReplaceExecutor(services, executor));

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(WebhookTestData.Publish(entityId)));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(await WebhookDatabaseTestData.ReadEntityAsync(host, entityId));
        Assert.Equal(0, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));
        Assert.Empty(await WebhookDatabaseTestData.ReadLogsAsync(host, entityId));
    }

    [Fact]
    public async Task RequestCancellationTokenPropagatesToTheProductionTransactionExecutor()
    {
        var productionExecutor = EventProcessingExecutorFactory.Create(_fixture.WriteConnectionString);
        var recordingExecutor = new CancellationRecordingEventTransactionExecutor(productionExecutor);
        await using var host = WebhookTestHost.Create(
            _fixture,
            services => ReplaceExecutor(services, recordingExecutor));

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(
                WebhookTestData.Publish(WebhookTestData.UniqueId("cancellation-token"))));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(recordingExecutor.ObservedCancelableToken);
    }

    [Fact]
    public async Task SameVersionConflictPreservesImmutableRevisionPayload()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("immutable-revision");
        var originalPayload = "{\"value\":1,\"name\":\"original\"}";
        var conflictingPayload = "{\"value\":2,\"name\":\"replacement\"}";

        using var firstResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(WebhookTestData.Publish(entityId, payload: originalPayload)));
        using var secondResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(
                WebhookTestData.Publish(
                    entityId,
                    eventId: WebhookTestData.UniqueId("immutable-event"),
                    payload: conflictingPayload)));
        using var secondJson = await WebhookTestData.ReadJsonAsync(secondResponse);
        var revision = await WebhookDatabaseTestData.ReadRevisionAsync(host, entityId, 1, 1);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal("conflict", secondJson.RootElement.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal(originalPayload, revision!.FirstObservedPayload);
        Assert.Equal(1, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));
    }

    [Theory]
    [InlineData("publish", "Published")]
    [InlineData("unpublish", "Unpublished")]
    public async Task DeleteThenPublishOrUnpublishRecreatesTheNextGeneration(
        string recreationType,
        string expectedStatus)
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("recreation");

        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Publish(entityId, version: 5, timestamp: "2026-08-02T10:00:00Z"),
            "applied");
        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Delete(entityId, timestamp: "2026-08-02T11:00:00Z"),
            "applied");
        var recreation = WebhookTestData.Publish(
            entityId,
            version: 2,
            eventId: WebhookTestData.UniqueId("recreation-event"),
            timestamp: "2026-08-02T12:00:00Z",
            payload: "{\"generation\":2}",
            type: recreationType);
        await PostAndAssertOutcomeAsync(host, recreation, "applied");

        var entity = await WebhookDatabaseTestData.ReadEntityAsync(host, entityId);
        var tombstone = await WebhookDatabaseTestData.ReadTombstoneAsync(host, entityId);

        Assert.Equal(2, entity!.Generation);
        Assert.Equal(2, entity.LatestVersion);
        Assert.Equal(expectedStatus, entity.CmsPublicationStatus);
        Assert.False(entity.AdministrativeDisabled);
        Assert.Equal(1, tombstone!.LastDeletedGeneration);
        Assert.Equal(1, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));
    }

    [Fact]
    public async Task AC057KeepsHighWatermarkAndUsesItForAllDeleteBoundariesThroughHttp()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("ac057-http");

        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Publish(entityId, version: 5, timestamp: "2026-08-02T10:00:00Z"),
            "applied");
        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Publish(
                entityId,
                version: 6,
                eventId: WebhookTestData.UniqueId("version-six"),
                timestamp: "2026-08-02T09:00:00Z",
                payload: "{\"version\":6}"),
            "applied");
        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Delete(
                entityId,
                eventId: WebhookTestData.UniqueId("delete-stale"),
                timestamp: "2026-08-02T09:30:00Z"),
            "stale");
        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Delete(
                entityId,
                eventId: WebhookTestData.UniqueId("delete-equal"),
                timestamp: "2026-08-02T10:00:00Z"),
            "conflict");

        var activeEntity = await WebhookDatabaseTestData.ReadEntityAsync(host, entityId);
        Assert.Equal(new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc), activeEntity!.CurrentVersionOccurredAtUtc);
        Assert.Equal(new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc), activeEntity.EntityEventHighWatermarkUtc);
        Assert.Equal(2, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));

        await PostAndAssertOutcomeAsync(
            host,
            WebhookTestData.Delete(
                entityId,
                eventId: WebhookTestData.UniqueId("delete-later"),
                timestamp: "2026-08-02T10:00:00.0000001Z"),
            "applied");

        Assert.Null(await WebhookDatabaseTestData.ReadEntityAsync(host, entityId));
        Assert.Equal(0, await WebhookDatabaseTestData.CountRevisionsAsync(host, entityId));
        Assert.Equal(5, (await WebhookDatabaseTestData.ReadLogsAsync(host, entityId)).Length);
    }

    private static int ReadSequence(JsonElement item)
    {
        return item.GetProperty("sequence").GetInt32();
    }

    private static string? ReadOutcome(JsonElement item)
    {
        return item.GetProperty("outcome").GetString();
    }

    private static IEnumerable<string?> ReadSafeLogText(
        CmsEventProcessingLog log)
    {
        yield return log.IdempotencyKey;
        yield return log.ExternalEventId;
        yield return log.EventType;
        yield return log.EntityId;
        yield return log.Outcome;
        yield return log.Code;
        yield return log.CorrelationId;
        yield return log.AuthenticatedCmsSubject;
    }

    private static void ReplaceExecutor(
        IServiceCollection services,
        IEventTransactionExecutor executor)
    {
        services.RemoveAll<IEventTransactionExecutor>();
        services.AddSingleton(executor);
    }

    private static async Task AssertReplayAsync(WebhookTestHost host, string eventJson)
    {
        using var firstResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(eventJson));
        using var secondResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(eventJson));
        using var secondJson = await WebhookTestData.ReadJsonAsync(secondResponse);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("duplicate", secondJson.RootElement.GetProperty("results")[0].GetProperty("outcome").GetString());
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
        Assert.Equal(
            expectedOutcome,
            json.RootElement.GetProperty("results")[0].GetProperty("outcome").GetString());
    }
}
