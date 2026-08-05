using System.Net;
using System.Text.Json;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CmsSync.IntegrationTests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class OpenApiDocumentTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    [Fact]
    public async Task SwaggerDocumentContainsBusinessRoutesSecurityAndRequestContracts()
    {
        await using var factory = CreateFactory("Development");
        using var client = CreateHttpsClient(factory);
        using var response = await client.GetAsync(
            "/swagger/v1/swagger.json",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var documentJson = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(documentJson);
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        var expectedBusinessPaths = new[]
        {
            "/cms/events",
            "/api/entities",
            "/api/entities/{entityId}",
            "/api/entities/{entityId}/administrative-state",
        };

        foreach (var expectedPath in expectedBusinessPaths)
        {
            Assert.True(paths.TryGetProperty(expectedPath, out _), $"Missing OpenAPI path: {expectedPath}");
        }

        var actualBusinessPaths = paths.EnumerateObject()
            .Select(path => path.Name)
            .Where(path =>
                path.StartsWith("/cms", StringComparison.Ordinal) ||
                path.StartsWith("/api/entities", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedBusinessPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(), actualBusinessPaths);

        var cmsPost = GetOperation(paths, "/cms/events", "post");
        var entitiesGet = GetOperation(paths, "/api/entities", "get");
        var entityByIdGet = GetOperation(paths, "/api/entities/{entityId}", "get");
        var administrativePut = GetOperation(paths, "/api/entities/{entityId}/administrative-state", "put");

        Assert.Equal("ProcessCmsEvents", cmsPost.GetProperty("operationId").GetString());
        Assert.Equal("ListCmsEntities", entitiesGet.GetProperty("operationId").GetString());
        Assert.Equal("GetCmsEntityById", entityByIdGet.GetProperty("operationId").GetString());
        Assert.Equal(
            "SetCmsEntityAdministrativeState",
            administrativePut.GetProperty("operationId").GetString());

        AssertResponseCodes(cmsPost, "200", "400", "401", "413", "415", "500", "503");
        AssertResponseCodes(entitiesGet, "200", "400", "401", "500");
        AssertResponseCodes(entityByIdGet, "200", "401", "404", "500");
        AssertResponseCodes(administrativePut, "200", "400", "401", "403", "404", "500", "503");

        Assert.True(
            cmsPost.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/*+json", out _),
            "Webhook operation does not document application/*+json.");
        var webhookRequestSchema = ResolveSchema(
            root,
            cmsPost.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        Assert.Equal("array", webhookRequestSchema.GetProperty("type").GetString());
        Assert.Equal(1, webhookRequestSchema.GetProperty("minItems").GetInt32());
        Assert.Equal(50, webhookRequestSchema.GetProperty("maxItems").GetInt32());

        var webhookItemSchema = ResolveSchema(root, webhookRequestSchema.GetProperty("items"));
        var webhookItemProperties = webhookItemSchema.GetProperty("properties");
        Assert.True(webhookItemProperties.TryGetProperty("id", out _));
        Assert.False(webhookItemProperties.TryGetProperty("entityId", out _));
        Assert.False(webhookItemProperties.TryGetProperty("events", out _));

        var administrativeRequestSchema = ResolveSchema(
            root,
            administrativePut.GetProperty("requestBody")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema"));
        var administrativeRequestProperties = administrativeRequestSchema.GetProperty("properties");
        Assert.True(administrativeRequestProperties.TryGetProperty("Disabled", out var disabledProperty));
        Assert.Equal("boolean", disabledProperty.GetProperty("type").GetString());
        Assert.False(administrativeRequestProperties.TryGetProperty("disabled", out _));

        var requiredAdministrativeProperties = administrativeRequestSchema.GetProperty("required")
            .EnumerateArray()
            .Select(entry => entry.GetString())
            .Where(entry => entry is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains("Disabled", requiredAdministrativeProperties);

        var entitiesQueryParameters = entitiesGet.GetProperty("parameters")
            .EnumerateArray()
            .ToArray();
        var pageSizeParameter = entitiesQueryParameters.Single(parameter =>
            string.Equals(parameter.GetProperty("name").GetString(), "pageSize", StringComparison.Ordinal));
        Assert.Equal("query", pageSizeParameter.GetProperty("in").GetString());
        Assert.False(
            pageSizeParameter.TryGetProperty("required", out var pageSizeRequired) &&
            pageSizeRequired.GetBoolean());
        var pageSizeSchema = ResolveSchema(root, pageSizeParameter.GetProperty("schema"));
        Assert.Equal("integer", pageSizeSchema.GetProperty("type").GetString());
        Assert.Equal(1, (int)pageSizeSchema.GetProperty("minimum").GetDouble());
        Assert.Equal(100, (int)pageSizeSchema.GetProperty("maximum").GetDouble());
        Assert.Equal(20, pageSizeSchema.GetProperty("default").GetInt32());

        var afterEntityIdParameter = entitiesQueryParameters.Single(parameter =>
            string.Equals(parameter.GetProperty("name").GetString(), "afterEntityId", StringComparison.Ordinal));
        Assert.Equal("query", afterEntityIdParameter.GetProperty("in").GetString());
        Assert.False(
            afterEntityIdParameter.TryGetProperty("required", out var afterEntityIdRequired) &&
            afterEntityIdRequired.GetBoolean());
        Assert.Equal(
            "string",
            ResolveSchema(root, afterEntityIdParameter.GetProperty("schema")).GetProperty("type").GetString());

        AssertPathParameter(entityByIdGet, "entityId");
        AssertPathParameter(administrativePut, "entityId");

        var securitySchemes = root.GetProperty("components").GetProperty("securitySchemes");
        AssertSecurityScheme(securitySchemes, "CmsBasic");
        AssertSecurityScheme(securitySchemes, "ConsumerBasic");

        Assert.Equal(["CmsBasic"], GetRequiredSecuritySchemes(cmsPost));
        Assert.Equal(["ConsumerBasic"], GetRequiredSecuritySchemes(entitiesGet));
        Assert.Equal(["ConsumerBasic"], GetRequiredSecuritySchemes(entityByIdGet));
        Assert.Equal(["ConsumerBasic"], GetRequiredSecuritySchemes(administrativePut));

        var allBusinessOperations = new[] { cmsPost, entitiesGet, entityByIdGet, administrativePut };
        Assert.All(
            allBusinessOperations,
            operation => Assert.True(
                GetRequiredSecuritySchemes(operation).Length == 1,
                "An operation unexpectedly requires multiple security schemes."));

        Assert.DoesNotContain("Authorization: Basic", documentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cmsservice1", documentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("normalconsumer", documentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("administrator", documentJson, StringComparison.Ordinal);
    }

    private static void AssertResponseCodes(JsonElement operation, params string[] expectedStatusCodes)
    {
        var responses = operation.GetProperty("responses");

        foreach (var expectedStatusCode in expectedStatusCodes)
        {
            Assert.True(
                responses.TryGetProperty(expectedStatusCode, out _),
                $"Expected response status code metadata was not found: {expectedStatusCode}");
        }
    }

    private static void AssertPathParameter(JsonElement operation, string parameterName)
    {
        var parameter = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(entry => string.Equals(
                entry.GetProperty("name").GetString(),
                parameterName,
                StringComparison.Ordinal));

        Assert.Equal("path", parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Equal("string", parameter.GetProperty("schema").GetProperty("type").GetString());
    }

    private static void AssertSecurityScheme(JsonElement securitySchemes, string schemeName)
    {
        Assert.True(securitySchemes.TryGetProperty(schemeName, out var scheme));
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("basic", scheme.GetProperty("scheme").GetString());
    }

    private static string[] GetRequiredSecuritySchemes(JsonElement operation)
    {
        if (!operation.TryGetProperty("security", out var securityRequirements))
        {
            return [];
        }

        return securityRequirements.EnumerateArray()
            .SelectMany(requirement => requirement.EnumerateObject().Select(property => property.Name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static JsonElement GetOperation(JsonElement paths, string path, string method)
    {
        Assert.True(paths.TryGetProperty(path, out var pathItem), $"Path was not found: {path}");
        Assert.True(pathItem.TryGetProperty(method, out var operation), $"Operation was not found: {method.ToUpperInvariant()} {path}");
        return operation;
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        var current = schema;

        for (var depth = 0; depth < 10; depth++)
        {
            if (!current.TryGetProperty("$ref", out var referenceElement))
            {
                return current;
            }

            var reference = referenceElement.GetString();
            Assert.False(string.IsNullOrWhiteSpace(reference));

            var segments = reference!.Split('/', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(segments.Length >= 3, $"Unexpected schema reference format: {reference}");

            var resolved = root;

            for (var segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
            {
                resolved = resolved.GetProperty(segments[segmentIndex]);
            }

            current = resolved;
        }

        throw new InvalidOperationException("Schema reference depth exceeded the supported traversal limit.");
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
