using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure.Authentication;
using CmsSync.Infrastructure.Persistence;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.Webhook;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "Webhook")]
public sealed class WebhookHttpContractTests
{
    private readonly SqlServerFixture _fixture;

    public WebhookHttpContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RawArraysWithOneAndFiftyItemsPreserveSequenceAndUseGeneratedBatchIds()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var singleEntity = WebhookTestData.UniqueId("one-item");

        using var singleResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(WebhookTestData.Publish(singleEntity)));
        using var singleJson = await WebhookTestData.ReadJsonAsync(singleResponse);

        Assert.Equal(HttpStatusCode.OK, singleResponse.StatusCode);
        Assert.NotEqual(Guid.Empty, singleJson.RootElement.GetProperty("batchId").GetGuid());
        Assert.Equal(0, singleJson.RootElement.GetProperty("results")[0].GetProperty("sequence").GetInt32());
        Assert.Equal(singleEntity, singleJson.RootElement.GetProperty("results")[0].GetProperty("id").GetString());

        var fiftyEvents = Enumerable.Range(0, 50)
            .Select(index => WebhookTestData.Publish(WebhookTestData.UniqueId($"fifty-{index}")))
            .ToArray();
        using var fiftyResponse = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(fiftyEvents));
        using var fiftyJson = await WebhookTestData.ReadJsonAsync(fiftyResponse);
        var results = fiftyJson.RootElement.GetProperty("results");

        Assert.Equal(HttpStatusCode.OK, fiftyResponse.StatusCode);
        Assert.Equal(50, results.GetArrayLength());
        Assert.Equal(Enumerable.Range(0, 50), results.EnumerateArray().Select(ReadSequence));
        Assert.Equal(50, fiftyJson.RootElement.GetProperty("summary").GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"value\"")]
    [InlineData("123")]
    [InlineData("[]")]
    [InlineData("{\"events\":[]}")]
    public async Task InvalidTopLevelEnvelopesReturn400WithoutProcessing(string body)
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var priorLogCount = await CountProcessingLogsAsync(host);

        using var response = await WebhookTestData.PostCmsAsync(host, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal(priorLogCount, await CountProcessingLogsAsync(host));
    }

    [Fact]
    public async Task FiftyOneItemsReturn400WithoutProcessing()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var events = Enumerable.Range(0, 51)
            .Select(index => WebhookTestData.Publish(WebhookTestData.UniqueId($"oversized-batch-{index}")))
            .ToArray();
        var priorLogCount = await CountProcessingLogsAsync(host);

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(events));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(priorLogCount, await CountProcessingLogsAsync(host));
    }

    [Fact]
    public async Task MalformedJsonReturns400ProblemDetailsWithoutProcessing()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var priorLogCount = await CountProcessingLogsAsync(host);

        using var response = await WebhookTestData.PostCmsAsync(host, "[{\"type\":");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertProblemCodeAsync(response, CmsEventParsingCodes.MalformedJson);
        Assert.Equal(priorLogCount, await CountProcessingLogsAsync(host));
    }

    [Fact]
    public async Task DuplicateEventAndPayloadPropertiesAreItemLevelInvalidResults()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var eventDuplicateId = WebhookTestData.UniqueId("duplicate-event-property");
        var payloadDuplicateId = WebhookTestData.UniqueId("duplicate-payload-property");
        var duplicateEventProperty = WebhookTestData.Publish(eventDuplicateId)
            .Insert(1, "\"type\":\"publish\",");
        var duplicatePayloadProperty = WebhookTestData.Publish(
            payloadDuplicateId,
            payload: "{\"value\":1,\"value\":2}");
        var body = WebhookTestData.Array(
            duplicateEventProperty,
            duplicatePayloadProperty);

        using var response = await WebhookTestData.PostCmsAsync(host, body);
        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 but received {(int)response.StatusCode}: {responseBody}");
        using var json = await WebhookTestData.ReadJsonAsync(response);
        var results = json.RootElement.GetProperty("results");

        Assert.All(results.EnumerateArray(), item => Assert.Equal("invalid", item.GetProperty("outcome").GetString()));
        Assert.All(results.EnumerateArray(), item => Assert.Equal("DUPLICATE_PROPERTY_NAME", item.GetProperty("code").GetString()));
        Assert.False(await EntityExistsAsync(host, eventDuplicateId));
        Assert.False(await EntityExistsAsync(host, payloadDuplicateId));
    }

    [Fact]
    public async Task UnknownEnvelopePropertiesAreIgnoredAndWireIdIsPreserved()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("unknown-property");
        var payload = "{\"nested\":{\"value\":1},\"items\":[true,null]}";
        var eventJson = WebhookTestData.Publish(entityId, payload: payload);
        eventJson = eventJson.Insert(eventJson.Length - 1, ",\"futureField\":{\"enabled\":true}");

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(eventJson));
        using var json = await WebhookTestData.ReadJsonAsync(response);
        var result = json.RootElement.GetProperty("results")[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(entityId, result.GetProperty("id").GetString());
        Assert.False(result.TryGetProperty("entityId", out _));
        Assert.False(result.TryGetProperty("payload", out _));
        Assert.Equal(payload, await ReadStoredPayloadAsync(host, entityId));
    }

    [Fact]
    public async Task EntityIdAliasIsRejectedWhileExactWireIdIsAccepted()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var rejectedId = WebhookTestData.UniqueId("entity-alias");
        var acceptedId = WebhookTestData.UniqueId("wire-id");
        var aliasEvent = WebhookTestData.Publish(rejectedId).Replace(
            "\"id\":",
            "\"entityId\":",
            StringComparison.Ordinal);

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(aliasEvent, WebhookTestData.Publish(acceptedId)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = await WebhookTestData.ReadJsonAsync(response);
        var results = json.RootElement.GetProperty("results");

        Assert.Equal("invalid", results[0].GetProperty("outcome").GetString());
        Assert.Equal("ENTITY_ID_REQUIRED", results[0].GetProperty("code").GetString());
        Assert.False(results[0].TryGetProperty("id", out _));
        Assert.Equal("applied", results[1].GetProperty("outcome").GetString());
        Assert.Equal(acceptedId, results[1].GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("publish", "publish")]
    [InlineData("Publish", "publish")]
    [InlineData("PUBLISH", "publish")]
    [InlineData("unpublish", "unpublish")]
    [InlineData("unPublish", "unpublish")]
    [InlineData("UnPublish", "unpublish")]
    [InlineData("UNPUBLISH", "unpublish")]
    [InlineData("delete", "delete")]
    [InlineData("Delete", "delete")]
    [InlineData("DELETE", "delete")]
    [InlineData("  Publish  ", "publish")]
    public async Task DocumentedEventTypeVariantsNormalizeThroughTheHttpPipeline(
        string suppliedType,
        string canonicalType)
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var entityId = WebhookTestData.UniqueId("event-type");
        var eventJson = canonicalType == "delete"
            ? WebhookTestData.Delete(entityId).Replace("\"delete\"", JsonSerializer.Serialize(suppliedType), StringComparison.Ordinal)
            : WebhookTestData.Publish(entityId, type: suppliedType);

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(eventJson));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(canonicalType, await ReadStoredEventTypeAsync(host, entityId));
    }

    [Fact]
    public async Task UnsupportedTypeIsAnItemLevelInvalidOutcome()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var eventJson = WebhookTestData.Publish(
            WebhookTestData.UniqueId("unsupported-type"),
            type: "archive");

        using var response = await WebhookTestData.PostCmsAsync(host, WebhookTestData.Array(eventJson));
        using var json = await WebhookTestData.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("invalid", json.RootElement.GetProperty("results")[0].GetProperty("outcome").GetString());
        Assert.Equal("EVENT_TYPE_INVALID", json.RootElement.GetProperty("results")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("{\"type\":\"publish\",\"id\":\"missing-version\",\"timestamp\":\"2026-08-02T10:00:00Z\",\"payload\":{}}", "VERSION_REQUIRED")]
    [InlineData("{\"type\":\"unpublish\",\"id\":\"missing-payload\",\"version\":1,\"timestamp\":\"2026-08-02T10:00:00Z\"}", "PAYLOAD_REQUIRED")]
    [InlineData("{\"type\":\"delete\",\"id\":\"delete-version\",\"version\":1,\"timestamp\":\"2026-08-02T10:00:00Z\"}", "VERSION_NOT_ALLOWED")]
    [InlineData("{\"type\":\"delete\",\"id\":\"delete-payload\",\"timestamp\":\"2026-08-02T10:00:00Z\",\"payload\":{}}", "PAYLOAD_NOT_ALLOWED")]
    public async Task FieldApplicabilityViolationsRemainItemLevelInvalid(
        string eventJson,
        string expectedCode)
    {
        await using var host = WebhookTestHost.Create(_fixture);

        using var response = await WebhookTestData.PostCmsAsync(host, WebhookTestData.Array(eventJson));
        using var json = await WebhookTestData.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("results")[0].GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/vnd.cms-events+json")]
    public async Task SupportedJsonMediaTypesAreAccepted(string mediaType)
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var body = WebhookTestData.Array(
            WebhookTestData.Publish(WebhookTestData.UniqueId("media-type")));

        using var response = await WebhookTestData.PostCmsAsync(host, body, mediaType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedNonJsonBodyReturns415BeforeEnvelopeValidation()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var priorLogCount = await CountProcessingLogsAsync(host);

        using var response = await WebhookTestData.PostCmsAsync(host, "not-json", "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        await AssertProblemCodeAsync(response, "UNSUPPORTED_MEDIA_TYPE");
        Assert.Equal(priorLogCount, await CountProcessingLogsAsync(host));
    }

    [Theory]
    [InlineData("text/plain", "not-json")]
    [InlineData("application/json", "[{\"type\":")]
    [InlineData("application/json", "{}")]
    public async Task UnauthenticatedRequestsDoNotRevealMediaOrEnvelopeValidation(
        string mediaType,
        string body)
    {
        await using var host = WebhookTestHost.Create(_fixture);
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookTestData.Route)
        {
            Content = WebhookTestData.CreateStringContent(body, mediaType),
        };

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            $"Basic realm=\"{AuthenticationConstants.CmsScheme}\"",
            response.Headers.WwwAuthenticate.Single().ToString());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConsumerCredentialsCannotAuthorizeTheWebhook()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        using var content = WebhookTestData.CreateStringContent("not-json", "text/plain");
        using var request = WebhookTestData.CreateAuthenticatedRequest(
            content,
            host.Credentials.ConsumerUsername,
            host.Credentials.ConsumerPassword);

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExactRequestLimitIsAcceptedAndOneAdditionalByteReturns413()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var acceptedBody = CreateRequestBodyWithExactSize(
            CmsEventIngestionLimits.AbsoluteMaximumRequestSizeBytes,
            WebhookTestData.UniqueId("request-limit"));
        var rejectedBody = new byte[acceptedBody.Length + 1];
        acceptedBody.CopyTo(rejectedBody, 0);
        rejectedBody[^1] = (byte)' ';

        using var acceptedRequest = WebhookTestData.CreateCmsRequest(
            host,
            WebhookTestData.CreateByteContent(acceptedBody));
        using var acceptedResponse = await host.Client.SendAsync(
            acceptedRequest,
            TestContext.Current.CancellationToken);
        using var rejectedRequest = WebhookTestData.CreateCmsRequest(
            host,
            WebhookTestData.CreateByteContent(rejectedBody));
        using var rejectedResponse = await host.Client.SendAsync(
            rejectedRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejectedResponse.StatusCode);
        await AssertProblemCodeAsync(rejectedResponse, CmsEventParsingCodes.RequestTooLarge);
    }

    [Fact]
    public async Task OversizedRequestTakesPrecedenceOverAuthentication()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var oversizedBody = new byte[CmsEventIngestionLimits.AbsoluteMaximumRequestSizeBytes + 1];
        using var request = new HttpRequestMessage(HttpMethod.Post, WebhookTestData.Route)
        {
            Content = WebhookTestData.CreateByteContent(oversizedBody),
        };

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task PayloadLimitAccepts256KiBAndRejectsOneAdditionalBytePerItem()
    {
        await using var host = WebhookTestHost.Create(_fixture);
        var acceptedId = WebhookTestData.UniqueId("payload-limit-accepted");
        var rejectedId = WebhookTestData.UniqueId("payload-limit-rejected");
        var acceptedPayload = CreatePayloadWithExactSize(CmsEventIngestionLimits.AbsoluteMaximumPayloadSizeBytes);
        var rejectedPayload = CreatePayloadWithExactSize(CmsEventIngestionLimits.AbsoluteMaximumPayloadSizeBytes + 1);

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(
                WebhookTestData.Publish(acceptedId, payload: acceptedPayload),
                WebhookTestData.Publish(rejectedId, payload: rejectedPayload)));
        using var json = await WebhookTestData.ReadJsonAsync(response);
        var results = json.RootElement.GetProperty("results");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("applied", results[0].GetProperty("outcome").GetString());
        Assert.Equal("invalid", results[1].GetProperty("outcome").GetString());
        Assert.Equal("PAYLOAD_TOO_LARGE", results[1].GetProperty("code").GetString());
        Assert.True(await EntityExistsAsync(host, acceptedId));
        Assert.False(await EntityExistsAsync(host, rejectedId));
    }

    private static int ReadSequence(JsonElement item)
    {
        return item.GetProperty("sequence").GetInt32();
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = await WebhookTestData.ReadJsonAsync(response);
        Assert.Equal((int)expectedStatus, json.RootElement.GetProperty("status").GetInt32());
        Assert.False(json.RootElement.TryGetProperty("stackTrace", out _));
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        string expectedCode)
    {
        using var json = await WebhookTestData.ReadJsonAsync(response);
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("exception", out _));
        Assert.False(json.RootElement.TryGetProperty("stackTrace", out _));
    }

    private static byte[] CreateRequestBodyWithExactSize(int size, string entityId)
    {
        var prefix = Encoding.UTF8.GetBytes(
            $"[{{\"type\":\"publish\",\"id\":\"{entityId}\",\"version\":1," +
            "\"timestamp\":\"2026-08-02T10:00:00Z\",\"payload\":{},\"padding\":\"");
        var suffix = Encoding.UTF8.GetBytes("\"}]");
        var paddingLength = size - prefix.Length - suffix.Length;
        Assert.True(paddingLength > 0);

        var body = new byte[size];
        prefix.CopyTo(body, 0);
        body.AsSpan(prefix.Length, paddingLength).Fill((byte)'x');
        suffix.CopyTo(body, prefix.Length + paddingLength);
        return body;
    }

    private static string CreatePayloadWithExactSize(int size)
    {
        const string prefix = "{\"value\":\"";
        const string suffix = "\"}";
        var textLength = size - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount(suffix);
        Assert.True(textLength >= 0);
        return prefix + new string('x', textLength) + suffix;
    }

    private static async Task<int> CountProcessingLogsAsync(WebhookTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs.CountAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> EntityExistsAsync(WebhookTestHost host, string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntities.AnyAsync(
            entity => entity.EntityId == entityId,
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> ReadStoredPayloadAsync(WebhookTestHost host, string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEntities
            .Where(entity => entity.EntityId == entityId)
            .Select(entity => entity.Payload)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string?> ReadStoredEventTypeAsync(WebhookTestHost host, string entityId)
    {
        using var scope = host.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CmsWriteDbContext>();
        return await context.CmsEventProcessingLogs
            .Where(log => log.EntityId == entityId)
            .OrderByDescending(log => log.ProcessingLogId)
            .Select(log => log.EventType)
            .FirstAsync(TestContext.Current.CancellationToken);
    }
}
