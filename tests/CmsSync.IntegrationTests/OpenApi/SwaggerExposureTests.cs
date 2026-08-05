using System.Net;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CmsSync.IntegrationTests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class SwaggerExposureTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    [Fact]
    public async Task DevelopmentEnvironmentExposesSwaggerUiAndDocument()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateHttpsClient(factory);

        using var swaggerRootResponse = await client.GetAsync(
            "/swagger",
            TestContext.Current.CancellationToken);

        Assert.True(
            swaggerRootResponse.StatusCode == HttpStatusCode.OK ||
            swaggerRootResponse.StatusCode == HttpStatusCode.MovedPermanently ||
            swaggerRootResponse.StatusCode == HttpStatusCode.Redirect ||
            swaggerRootResponse.StatusCode == HttpStatusCode.TemporaryRedirect,
            $"Unexpected /swagger status code: {(int)swaggerRootResponse.StatusCode}");

        if (swaggerRootResponse.Headers.Location is not null)
        {
            var redirectLocation = swaggerRootResponse.Headers.Location.ToString();

            Assert.Contains(
                "swagger/index.html",
                redirectLocation,
                StringComparison.Ordinal);
        }

        using var swaggerUiResponse = await client.GetAsync(
            "/swagger/index.html",
            TestContext.Current.CancellationToken);
        var swaggerUiHtml = await swaggerUiResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, swaggerUiResponse.StatusCode);
        Assert.Contains(
            "text/html",
            swaggerUiResponse.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Swagger UI", swaggerUiHtml, StringComparison.OrdinalIgnoreCase);

        using var swaggerDocumentResponse = await client.GetAsync(
            "/swagger/v1/swagger.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, swaggerDocumentResponse.StatusCode);
        Assert.Contains(
            "application/json",
            swaggerDocumentResponse.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonDevelopmentEnvironmentDoesNotMapSwaggerRoutes()
    {
        await using var factory = CreateFactory("Production");
        using var client = CreateHttpsClient(factory);

        foreach (var route in new[] { "/swagger", "/swagger/index.html", "/swagger/v1/swagger.json" })
        {
            using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static HttpClient CreateHttpsClient(CmsSyncWebApplicationFactory factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });
    }

    private static CmsSyncWebApplicationFactory CreateFactory(string environmentName)
    {
        return new CmsSyncWebApplicationFactory(
            SafeConnectionString,
            SafeConnectionString,
            environmentName: environmentName);
    }
}
