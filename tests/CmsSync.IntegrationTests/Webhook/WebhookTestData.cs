using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CmsSync.IntegrationTests.EventIngestion;
using CmsSync.IntegrationTests.TestHost;
using Xunit;

namespace CmsSync.IntegrationTests.Webhook;

internal static class WebhookTestData
{
    public const string Route = "/cms/events";

    public static string Publish(
        string entityId,
        long version = 1,
        string? eventId = null,
        string timestamp = EventProcessingTestData.DefaultTimestamp,
        string payload = "{\"value\":1}",
        string type = "publish")
    {
        return EventProcessingTestData.Publish(
            entityId,
            version,
            eventId,
            timestamp,
            payload,
            type);
    }

    public static string Unpublish(
        string entityId,
        long version = 1,
        string? eventId = null,
        string timestamp = EventProcessingTestData.DefaultTimestamp,
        string payload = "{\"value\":1}",
        string type = "unpublish")
    {
        return Publish(entityId, version, eventId, timestamp, payload, type);
    }

    public static string Delete(
        string entityId,
        string? eventId = null,
        string timestamp = EventProcessingTestData.DefaultTimestamp)
    {
        return EventProcessingTestData.Delete(entityId, eventId, timestamp);
    }

    public static string Array(params string[] events)
    {
        return $"[{string.Join(',', events)}]";
    }

    public static StringContent CreateStringContent(
        string body,
        string mediaType = "application/json")
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return content;
    }

    public static ByteArrayContent CreateByteContent(
        byte[] body,
        string mediaType = "application/json")
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(mediaType);
        return content;
    }

    public static HttpRequestMessage CreateCmsRequest(
        WebhookTestHost host,
        HttpContent content)
    {
        return CreateAuthenticatedRequest(
            content,
            host.Credentials.CmsUsername,
            host.Credentials.CmsPassword);
    }

    public static HttpRequestMessage CreateAuthenticatedRequest(
        HttpContent content,
        string username,
        string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Route)
        {
            Content = content,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            AuthenticationRequestFactory.CreateBasicParameter(username, password));
        return request;
    }

    public static async Task<HttpResponseMessage> PostCmsAsync(
        WebhookTestHost host,
        string body,
        string mediaType = "application/json",
        HttpClient? client = null)
    {
        using var request = CreateCmsRequest(host, CreateStringContent(body, mediaType));
        return await (client ?? host.Client).SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        return JsonDocument.Parse(bytes);
    }

    public static string UniqueId(string prefix)
    {
        return EventProcessingTestData.UniqueId(prefix);
    }
}
