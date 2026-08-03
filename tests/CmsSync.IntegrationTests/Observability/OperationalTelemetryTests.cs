using System.Net;
using CmsSync.Api.Observability;
using CmsSync.Application.Observability;
using CmsSync.Infrastructure.Persistence;
using CmsSync.Infrastructure.Persistence.Models;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using CmsSync.IntegrationTests.Webhook;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.Observability;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "Observability")]
public sealed class OperationalTelemetryTests
{
    private static readonly string[] DeterministicOutcomes =
        ["applied", "duplicate", "equivalent", "stale", "invalid", "conflict"];

    private readonly SqlServerFixture _fixture;

    public OperationalTelemetryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProcessingLogsAndApplicationLogsContainOnlySafeOperationalMetadata()
    {
        using var capturedLogs = new CapturedLogProvider();
        await using var host = WebhookTestHost.Create(_fixture, capturedLogs: capturedLogs);
        var correlationId = $"t014-{Guid.NewGuid():N}";
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        var entityId = WebhookTestData.UniqueId("telemetry-safe");
        var payloadSentinel = $"payload-{Guid.NewGuid():N}";
        var exceptionSentinel = $"exception-{Guid.NewGuid():N}";
        var appliedEventId = WebhookTestData.UniqueId("telemetry-applied");
        var applied = WebhookTestData.Publish(
            entityId,
            version: 2,
            eventId: appliedEventId,
            payload: $"{{\"secret\":\"{payloadSentinel}\"}}");
        var body = WebhookTestData.Array(
            applied,
            applied,
            WebhookTestData.Publish(
                entityId,
                version: 2,
                eventId: WebhookTestData.UniqueId("telemetry-equivalent"),
                payload: $"{{\"secret\":\"{payloadSentinel}\"}}"),
            WebhookTestData.Publish(
                entityId,
                version: 1,
                eventId: WebhookTestData.UniqueId("telemetry-stale")),
            WebhookTestData.Publish(
                WebhookTestData.UniqueId("telemetry-invalid"),
                type: "unsupported"),
            WebhookTestData.Publish(
                entityId,
                version: 2,
                eventId: WebhookTestData.UniqueId("telemetry-conflict"),
                payload: "{\"different\":true}"));
        using var request = WebhookTestData.CreateCmsRequest(host, WebhookTestData.CreateStringContent(body));
        request.Headers.Add(CorrelationContextMiddleware.HeaderName, correlationId);
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var logs = await ReadBatchLogsAsync(host, response);
        var sensitiveValues = new[]
        {
            payloadSentinel,
            exceptionSentinel,
            host.Credentials.CmsPassword,
            host.Credentials.ConsumerPassword,
            host.Credentials.AdministratorPassword,
            request.Headers.Authorization?.ToString() ?? string.Empty,
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString,
        };

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(correlationId, response.Headers.GetValues(CorrelationContextMiddleware.HeaderName).Single());
        Assert.Equal(6, logs.Length);
        Assert.Equal(Enumerable.Range(0, 6), logs.Select(log => log.Sequence));
        Assert.Equal(DeterministicOutcomes, logs.Select(log => log.Outcome.ToLowerInvariant()));
        Assert.All(logs, log =>
        {
            Assert.NotEqual(Guid.Empty, log.BatchId);
            Assert.False(string.IsNullOrWhiteSpace(log.Code));
            Assert.Equal(correlationId, log.CorrelationId);
        });
        Assert.All(
            logs.Where(log => log.Sequence != 4),
            log => Assert.Equal("publish", log.EventType));
        Assert.DoesNotContain(
            typeof(CmsEventProcessingLog).GetProperties(),
            property => property.Name.Contains("Payload", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(property.Name, "PayloadHash", StringComparison.Ordinal));
        Assert.False(capturedLogs.ContainsAny(sensitiveValues));
        Assert.Contains(
            capturedLogs.Entries,
            entry => entry.Message.Contains("CMS event batch completed", StringComparison.Ordinal) &&
                     entry.Message.Contains("ElapsedMilliseconds", StringComparison.Ordinal));
        Assert.All(DeterministicOutcomes, outcome =>
            Assert.Contains(
                capturedLogs.Entries,
                entry => entry.Message.Contains($"Outcome {outcome}", StringComparison.Ordinal) &&
                         entry.Message.Contains("Sequence", StringComparison.Ordinal) &&
                         entry.Message.Contains("Code", StringComparison.Ordinal) &&
                         entry.Message.Contains("ElapsedMilliseconds", StringComparison.Ordinal)));
        Assert.Contains(
            capturedLogs.Entries.SelectMany(entry => entry.Scopes),
            scope => scope.Contains(correlationId, StringComparison.Ordinal));
        Assert.Contains(
            capturedLogs.Entries.SelectMany(entry => entry.Scopes),
            scope => scope.Contains(traceId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsafeInboundCorrelationIsReplacedAndTheGeneratedValueIsPersisted()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        const string unsafeCorrelation = "invalid/correlation";
        using var request = WebhookTestData.CreateCmsRequest(
            host,
            WebhookTestData.CreateStringContent(
                WebhookTestData.Array(
                    WebhookTestData.Publish(WebhookTestData.UniqueId("generated-correlation")))));
        request.Headers.Add(CorrelationContextMiddleware.HeaderName, unsafeCorrelation);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var generatedCorrelation = response.Headers
            .GetValues(CorrelationContextMiddleware.HeaderName)
            .Single();
        var logs = await ReadBatchLogsAsync(host, response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(unsafeCorrelation, generatedCorrelation);
        Assert.Equal(32, generatedCorrelation.Length);
        Assert.All(generatedCorrelation, character => Assert.True(char.IsAsciiHexDigit(character)));
        Assert.Equal(generatedCorrelation, Assert.Single(logs).CorrelationId);
    }

    [Fact]
    public async Task MetricsCoverEveryOutcomeLatencyCodesAndAuthenticationWithoutHighCardinalityTags()
    {
        using var metrics = new MetricCollector();
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("telemetry-metrics");
        var appliedEventId = WebhookTestData.UniqueId("metrics-applied");
        var applied = WebhookTestData.Publish(entityId, version: 2, eventId: appliedEventId);
        var body = WebhookTestData.Array(
            applied,
            applied,
            WebhookTestData.Publish(
                entityId,
                version: 2,
                eventId: WebhookTestData.UniqueId("metrics-equivalent")),
            WebhookTestData.Publish(
                entityId,
                version: 1,
                eventId: WebhookTestData.UniqueId("metrics-stale")),
            WebhookTestData.Publish(WebhookTestData.UniqueId("metrics-invalid"), type: "unsupported"),
            WebhookTestData.Publish(
                entityId,
                version: 2,
                eventId: WebhookTestData.UniqueId("metrics-conflict"),
                payload: "{\"different\":true}"));

        using var response = await WebhookTestData.PostCmsAsync(host, body);
        using var unauthenticatedResponse = await host.Client.GetAsync(
            "/api/entities",
            TestContext.Current.CancellationToken);
        var measurements = metrics.Measurements;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
        Assert.Contains(measurements, item => item.InstrumentName == CmsOperationalMetrics.BatchCountInstrument);
        Assert.Contains(measurements, item => item.InstrumentName == CmsOperationalMetrics.BatchLatencyInstrument);
        Assert.Contains(measurements, item => item.InstrumentName == CmsOperationalMetrics.EventLatencyInstrument);
        Assert.All(DeterministicOutcomes, outcome =>
            Assert.Contains(
                measurements,
                item => item.InstrumentName == CmsOperationalMetrics.EventCountInstrument &&
                        string.Equals(ReadTag(item, "outcome"), outcome, StringComparison.Ordinal)));
        Assert.Contains(
            measurements,
            item => item.InstrumentName == CmsOperationalMetrics.OutcomeCodeInstrument &&
                    string.Equals(ReadTag(item, "outcome"), "invalid", StringComparison.Ordinal));
        Assert.Contains(
            measurements,
            item => item.InstrumentName == CmsOperationalMetrics.OutcomeCodeInstrument &&
                    string.Equals(ReadTag(item, "outcome"), "conflict", StringComparison.Ordinal));
        Assert.Contains(
            measurements,
            item => item.InstrumentName == CmsOperationalMetrics.AuthenticationFailureInstrument &&
                    string.Equals(ReadTag(item, "scheme"), "ConsumerBasic", StringComparison.Ordinal));

        var forbiddenTags = new[]
        {
            "entityId",
            "eventId",
            "batchId",
            "username",
            "correlationId",
            "traceId",
            "exception",
            "payload",
            "path",
        };
        Assert.DoesNotContain(
            measurements.SelectMany(item => item.Tags.Keys),
            tag => forbiddenTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
    }

    private static string? ReadTag(MetricMeasurement measurement, string name)
    {
        return measurement.Tags.TryGetValue(name, out var value)
            ? value?.ToString()
            : null;
    }

    private static async Task<CmsEventProcessingLog[]> ReadBatchLogsAsync(
        WebhookTestHost host,
        HttpResponseMessage response)
    {
        using var json = await WebhookTestData.ReadJsonAsync(response);
        var batchId = json.RootElement.GetProperty("batchId").GetGuid();
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs
            .AsNoTracking()
            .Where(log => log.BatchId == batchId)
            .OrderBy(log => log.Sequence)
            .ToArrayAsync(TestContext.Current.CancellationToken);
    }
}
