using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CmsSync.Api.Observability;
using CmsSync.Application.EventIngestion;
using CmsSync.IntegrationTests.Infrastructure;
using CmsSync.IntegrationTests.TestHost;
using CmsSync.IntegrationTests.Webhook;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CmsSync.IntegrationTests.Security;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "Security")]
public sealed class OperationalSecurityTests
{
    private readonly SqlServerFixture _fixture;

    public OperationalSecurityTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GlobalBoundaryReturnsSafe500WithoutLoggingOrReturningInternalDetails()
    {
        var internalSentinel = $"SQL-stack-Password={Guid.NewGuid():N}";
        var payloadSentinel = $"payload-{Guid.NewGuid():N}";
        using var capturedLogs = new CapturedLogProvider();
        await using var host = WebhookTestHost.Create(
            _fixture,
            services => ReplaceExecutor(
                services,
                new ThrowingEventTransactionExecutor(
                    () => new InvalidOperationException(internalSentinel))),
            capturedLogs);
        using var request = WebhookTestData.CreateCmsRequest(
            host,
            WebhookTestData.CreateStringContent(
                WebhookTestData.Array(
                    WebhookTestData.Publish(
                        WebhookTestData.UniqueId("global-500"),
                        payload: $"{{\"secret\":\"{payloadSentinel}\"}}"))));
        request.Headers.Add(CorrelationContextMiddleware.HeaderName, "safe-global-500");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("UNEXPECTED_PROCESSING_FAILURE", body, StringComparison.Ordinal);
        Assert.Equal(
            "safe-global-500",
            response.Headers.GetValues(CorrelationContextMiddleware.HeaderName).Single());
        Assert.DoesNotContain(internalSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain(payloadSentinel, body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlException", body, StringComparison.Ordinal);
        Assert.False(capturedLogs.ContainsAny([internalSentinel, payloadSentinel]));
        Assert.Contains(
            capturedLogs.Entries,
            entry => entry.Message.Contains(
                "Unhandled request failure converted to Problem Details",
                StringComparison.Ordinal));
        AssertRequestCompletedWithFinalStatus(capturedLogs, StatusCodes.Status500InternalServerError);
        AssertNoStore(response);
    }

    [Fact]
    public async Task GlobalBoundaryReturnsSafe503OnlyForRecognizedDependencyFailure()
    {
        using var capturedLogs = new CapturedLogProvider();
        await using var host = WebhookTestHost.Create(
            _fixture,
            services => ReplaceExecutor(
                services,
                new ThrowingEventTransactionExecutor(
                    static () => new EventProcessingDependencyUnavailableException())),
            capturedLogs);

        using var response = await WebhookTestData.PostCmsAsync(
            host,
            WebhookTestData.Array(
                WebhookTestData.Publish(WebhookTestData.UniqueId("global-503"))));
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("DEPENDENCY_UNAVAILABLE", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EventProcessingDependencyUnavailableException", body, StringComparison.Ordinal);
        AssertRequestCompletedWithFinalStatus(capturedLogs, StatusCodes.Status503ServiceUnavailable);
        AssertNoStore(response);
    }

    [Fact]
    public async Task Applicable400Through503ResponsesAreNoStoreAndKeepAuthenticationIsolation()
    {
        await using var factory = new CmsSyncWebApplicationFactory(
            _fixture.WriteConnectionString,
            _fixture.ReadConnectionString);
        using var client = factory.CreateClient();

        using var badRequest = AuthenticationRequestFactory.CreateBasicGet(
            "/api/entities?pageSize=0",
            factory.Credentials.ConsumerUsername,
            factory.Credentials.ConsumerPassword);
        using var badResponse = await client.SendAsync(badRequest, TestContext.Current.CancellationToken);

        using var unauthorizedResponse = await client.GetAsync(
            "/api/entities",
            TestContext.Current.CancellationToken);

        using var forbiddenRequest = CreateAdministratorStateRequest(
            factory.Credentials.ConsumerUsername,
            factory.Credentials.ConsumerPassword);
        using var forbiddenResponse = await client.SendAsync(
            forbiddenRequest,
            TestContext.Current.CancellationToken);

        using var notFoundRequest = AuthenticationRequestFactory.CreateBasicGet(
            $"/api/entities/{Guid.NewGuid():N}",
            factory.Credentials.AdministratorUsername,
            factory.Credentials.AdministratorPassword);
        using var notFoundResponse = await client.SendAsync(
            notFoundRequest,
            TestContext.Current.CancellationToken);

        using var unsupportedRequest = CreateCmsPost(
            factory,
            "not-json",
            "text/plain");
        using var unsupportedResponse = await client.SendAsync(
            unsupportedRequest,
            TestContext.Current.CancellationToken);

        var oversizedBody = new byte[CmsEventIngestionLimits.AbsoluteMaximumRequestSizeBytes + 1];
        using var oversizedRequest = new HttpRequestMessage(HttpMethod.Post, WebhookTestData.Route)
        {
            Content = new ByteArrayContent(oversizedBody),
        };
        oversizedRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        using var oversizedResponse = await client.SendAsync(
            oversizedRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, unsupportedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        Assert.Equal("Basic realm=\"ConsumerBasic\"", unauthorizedResponse.Headers.WwwAuthenticate.Single().ToString());
        Assert.Empty(forbiddenResponse.Headers.WwwAuthenticate);
        Assert.All(
            new[]
            {
                badResponse,
                unauthorizedResponse,
                forbiddenResponse,
                notFoundResponse,
                unsupportedResponse,
                oversizedResponse,
            },
            AssertNoStore);
    }

    private static HttpRequestMessage CreateAdministratorStateRequest(
        string username,
        string password)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/entities/{Guid.NewGuid():N}/administrative-state")
        {
            Content = new StringContent("{\"Disabled\":true}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            AuthenticationRequestFactory.CreateBasicParameter(username, password));
        return request;
    }

    private static HttpRequestMessage CreateCmsPost(
        CmsSyncWebApplicationFactory factory,
        string body,
        string mediaType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookTestData.Route)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            AuthenticationRequestFactory.CreateBasicParameter(
                factory.Credentials.CmsUsername,
                factory.Credentials.CmsPassword));
        return request;
    }

    private static void ReplaceExecutor(
        IServiceCollection services,
        IEventTransactionExecutor executor)
    {
        services.RemoveAll<IEventTransactionExecutor>();
        services.AddSingleton(executor);
    }

    private static void AssertNoStore(HttpResponseMessage response)
    {
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString() ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "no-cache",
            response.Headers.Pragma.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRequestCompletedWithFinalStatus(
        CapturedLogProvider capturedLogs,
        int expectedStatusCode)
    {
        var completionLog = Assert.Single(
            capturedLogs.Entries,
            entry => entry.EventId.Id == 1403);
        Assert.Contains($"StatusCode {expectedStatusCode}", completionLog.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusCode 200", completionLog.Message, StringComparison.Ordinal);
    }
}
