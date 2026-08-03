using System.Net;
using CmsSync.Application.AdministrativeState;
using CmsSync.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CmsSync.IntegrationTests.AdministrativeState;

[Collection(SqlServerIntegrationCollectionDefinition.Name)]
[Trait("Category", "AdministrativeState")]
public sealed class AdministrativeStateContractTests
{
    private readonly SqlServerFixture _fixture;

    public AdministrativeStateContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"value\"")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"Disabled\":null}")]
    [InlineData("{\"Disabled\":\"true\"}")]
    [InlineData("{\"disabled\":true}")]
    [InlineData("{\"DISABLED\":true}")]
    [InlineData("{\"Disabled\":tru")]
    public async Task BodyRequiresAnObjectWithExactBooleanDisabledProperty(string json)
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("invalid-body");
        using var request = host.CreateAdministratorPut(entityId, json);

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await AdministrativeStateResponseAssertions.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_ADMINISTRATIVE_STATE_REQUEST", body.GetProperty("code").GetString());
        Assert.DoesNotContain(entityId, body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("SQL", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack trace", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        AdministrativeStateResponseAssertions.AssertNoStore(response);
    }

    [Fact]
    public async Task UnknownPropertiesAreIgnoredAndResponseContainsOnlyLocalAdministrativeState()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("response");
        await AdministrativeStateTestData.SeedEntityAsync(host, entityId);
        using var request = host.CreateAdministratorPut(
            entityId,
            "{\"Disabled\":true,\"future\":{\"ignored\":true}}");

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await AdministrativeStateResponseAssertions.ReadJsonAsync(response);
        var propertyNames = body.EnumerateObject().Select(property => property.Name).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            [
                "id",
                "administrativeDisabled",
                "administrativeStateChangedAtUtc",
                "administrativeStateChangedBy",
            ],
            propertyNames);
        Assert.Equal(entityId, body.GetProperty("id").GetString());
        Assert.True(body.GetProperty("administrativeDisabled").GetBoolean());
        Assert.EndsWith(
            "Z",
            body.GetProperty("administrativeStateChangedAtUtc").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            host.Credentials.AdministratorUsername,
            body.GetProperty("administrativeStateChangedBy").GetString());
        Assert.DoesNotContain("rowVersion", propertyNames);
        Assert.DoesNotContain("payload", propertyNames);
        Assert.DoesNotContain("cmsPublicationStatus", propertyNames);
        AdministrativeStateResponseAssertions.AssertNoStore(response);
    }

    [Fact]
    public async Task AdministratorCanSetAndClearTheLocalOverride()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("set-clear");
        await AdministrativeStateTestData.SeedEntityAsync(host, entityId);

        using var disableRequest = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");
        using var disableResponse = await host.Client.SendAsync(
            disableRequest,
            TestContext.Current.CancellationToken);
        var disabledBody = await AdministrativeStateResponseAssertions.ReadJsonAsync(disableResponse);

        using var enableRequest = host.CreateAdministratorPut(entityId, "{\"Disabled\":false}");
        using var enableResponse = await host.Client.SendAsync(
            enableRequest,
            TestContext.Current.CancellationToken);
        var enabledBody = await AdministrativeStateResponseAssertions.ReadJsonAsync(enableResponse);
        var persisted = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));

        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        Assert.True(disabledBody.GetProperty("administrativeDisabled").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
        Assert.False(enabledBody.GetProperty("administrativeDisabled").GetBoolean());
        Assert.False(persisted.AdministrativeDisabled);
        Assert.NotNull(persisted.AdministrativeStateChangedAtUtc);
        Assert.Equal(host.Credentials.AdministratorUsername, persisted.AdministrativeStateChangedBy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepeatingCurrentValueDoesNotRewriteAuditOrRowVersion(bool disabled)
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("idempotent");
        await AdministrativeStateTestData.SeedEntityAsync(host, entityId);

        await SetStateAsync(host, entityId, disabled: true);

        if (!disabled)
        {
            await SetStateAsync(host, entityId, disabled: false);
        }

        var beforeRepeat = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));
        using var repeatRequest = host.CreateAdministratorPut(
            entityId,
            $"{{\"Disabled\":{disabled.ToString().ToLowerInvariant()}}}");
        using var repeatResponse = await host.Client.SendAsync(
            repeatRequest,
            TestContext.Current.CancellationToken);
        var repeatBody = await AdministrativeStateResponseAssertions.ReadJsonAsync(repeatResponse);
        var afterRepeat = Assert.IsType<CmsSync.Infrastructure.Persistence.Models.CmsEntity>(
            await AdministrativeStateTestData.ReadEntityAsync(host, entityId));

        Assert.Equal(HttpStatusCode.OK, repeatResponse.StatusCode);
        Assert.Equal(disabled, repeatBody.GetProperty("administrativeDisabled").GetBoolean());
        Assert.Equal(beforeRepeat.RowVersion, afterRepeat.RowVersion);
        Assert.Equal(
            beforeRepeat.AdministrativeStateChangedAtUtc,
            afterRepeat.AdministrativeStateChangedAtUtc);
        Assert.Equal(
            beforeRepeat.AdministrativeStateChangedBy,
            afterRepeat.AdministrativeStateChangedBy);
        Assert.Equal(host.Credentials.AdministratorUsername, afterRepeat.AdministrativeStateChangedBy);
    }

    [Fact]
    public async Task AuthenticationUsesAdministratorAccessAndConsumerBasicChallenge()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var entityId = AdministrativeStateTestData.UniqueId("authorization");
        const string body = "{\"Disabled\":true}";
        using var consumerRequest = host.CreateConsumerPut(entityId, body);
        using var missingRequest = AdministrativeStateTestHost.CreateUnauthenticatedPut(entityId, body);
        using var malformedRequest = AdministrativeStateTestHost.CreatePutWithAuthorization(
            entityId,
            body,
            "Basic",
            "not-base64");
        using var cmsRequest = host.CreateCmsPut(entityId, body);

        using var consumerResponse = await host.Client.SendAsync(
            consumerRequest,
            TestContext.Current.CancellationToken);
        using var missingResponse = await host.Client.SendAsync(
            missingRequest,
            TestContext.Current.CancellationToken);
        using var malformedResponse = await host.Client.SendAsync(
            malformedRequest,
            TestContext.Current.CancellationToken);
        using var cmsResponse = await host.Client.SendAsync(
            cmsRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, consumerResponse.StatusCode);
        Assert.Empty(consumerResponse.Headers.WwwAuthenticate);
        AdministrativeStateResponseAssertions.AssertNoStore(consumerResponse);

        foreach (var response in new[] { missingResponse, malformedResponse, cmsResponse })
        {
            AdministrativeStateResponseAssertions.AssertConsumerChallenge(response);
            AdministrativeStateResponseAssertions.AssertNoStore(response);
        }
    }

    [Fact]
    public async Task UnknownAndDeletedEntitiesUseIndistinguishableNotFoundResponses()
    {
        await using var host = AdministrativeStateTestHost.Create(_fixture);
        var deletedId = AdministrativeStateTestData.UniqueId("deleted");
        var unknownId = AdministrativeStateTestData.UniqueId("unknown");
        using var deleteResponse = await AdministrativeStateTestData.SendCmsEventAsync(
            host,
            AdministrativeStateTestData.Delete(deletedId, "2026-08-03T11:00:00Z"));
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var normalizedProblems = new List<Dictionary<string, string>>();

        foreach (var entityId in new[] { deletedId, unknownId })
        {
            using var request = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");
            using var response = await host.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken);
            var body = await AdministrativeStateResponseAssertions.ReadJsonAsync(response);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(entityId, body.GetRawText(), StringComparison.Ordinal);
            AdministrativeStateResponseAssertions.AssertNoStore(response);
            normalizedProblems.Add(body
                .EnumerateObject()
                .Where(property => !string.Equals(property.Name, "traceId", StringComparison.Ordinal))
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.ToString(),
                    StringComparer.Ordinal));
        }

        Assert.Equal(normalizedProblems[0], normalizedProblems[1]);
    }

    [Fact]
    public async Task UnexpectedFailuresReturnSafeProblemDetails()
    {
        await using var host = AdministrativeStateTestHost.Create(
            _fixture,
            services =>
            {
                services.RemoveAll<IAdministrativeStateService>();
                services.AddScoped<IAdministrativeStateService, ThrowingAdministrativeStateService>();
            });
        var entityId = AdministrativeStateTestData.UniqueId("failure");
        using var request = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("ADMINISTRATIVE_STATE_UPDATE_FAILED", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CmsEntities", body, StringComparison.Ordinal);
        Assert.DoesNotContain("RowVersion", body, StringComparison.Ordinal);
        Assert.DoesNotContain("stack trace", body, StringComparison.OrdinalIgnoreCase);
        AdministrativeStateResponseAssertions.AssertNoStore(response);
    }

    [Fact]
    public async Task RequestCancellationIsPropagatedToTheApplicationService()
    {
        var probe = new CancellationProbeAdministrativeStateService();
        await using var host = AdministrativeStateTestHost.Create(
            _fixture,
            services =>
            {
                services.RemoveAll<IAdministrativeStateService>();
                services.AddScoped<IAdministrativeStateService>(_ => probe);
            });
        var entityId = AdministrativeStateTestData.UniqueId("cancellation");
        using var request = host.CreateAdministratorPut(entityId, "{\"Disabled\":true}");
        using var cancellationSource = new CancellationTokenSource();

        var sendTask = host.Client.SendAsync(request, cancellationSource.Token);
        await probe.Started.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
        Assert.True(probe.CancellationObserved);
    }

    private static async Task SetStateAsync(
        AdministrativeStateTestHost host,
        string entityId,
        bool disabled)
    {
        using var request = host.CreateAdministratorPut(
            entityId,
            $"{{\"Disabled\":{disabled.ToString().ToLowerInvariant()}}}");
        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
