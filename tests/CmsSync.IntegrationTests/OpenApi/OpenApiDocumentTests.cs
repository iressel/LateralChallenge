using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CmsSync.IntegrationTests.OpenApi;

[Trait("Category", "OpenApi")]
public sealed class OpenApiDocumentTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    private static readonly Regex CmsTestUsernamePattern = new(
        @"\bcms-[0-9a-f]{12}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ConsumerTestUsernamePattern = new(
        @"\bconsumer-[0-9a-f]{32}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AdministratorTestUsernamePattern = new(
        @"\badministrator-[0-9a-f]{32}\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        var allBusinessOperations = new[] { cmsPost, entitiesGet, entityByIdGet, administrativePut };

        Assert.Equal(["CmsEvents"], GetOperationTags(cmsPost));
        Assert.Equal(["CmsEntities"], GetOperationTags(entitiesGet));
        Assert.Equal(["CmsEntities"], GetOperationTags(entityByIdGet));
        Assert.Equal(["CmsEntities"], GetOperationTags(administrativePut));

        var distinctOperationTags = allBusinessOperations
            .SelectMany(GetOperationTags)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["CmsEntities", "CmsEvents"], distinctOperationTags);
        Assert.DoesNotContain(
            allBusinessOperations.SelectMany(GetOperationTags),
            tag => tag.Length > 0 && tag[0] == '#');
        Assert.DoesNotContain(
            allBusinessOperations.SelectMany(GetOperationTags),
            tag => tag.Contains("/components/tags/", StringComparison.Ordinal));
        Assert.DoesNotContain(
            allBusinessOperations.SelectMany(GetOperationTags),
            tag => string.Equals(tag, "CMS Entities", StringComparison.Ordinal));
        Assert.DoesNotContain(
            allBusinessOperations.SelectMany(GetOperationTags),
            tag => string.Equals(tag, "CMS Events", StringComparison.Ordinal));

        Assert.DoesNotContain("#/components/tags/CMS Entities", documentJson, StringComparison.Ordinal);
        Assert.DoesNotContain("#/components/tags/CMS Events", documentJson, StringComparison.Ordinal);

        Assert.Equal("ProcessCmsEvents", cmsPost.GetProperty("operationId").GetString());
        Assert.Equal("ListCmsEntities", entitiesGet.GetProperty("operationId").GetString());
        Assert.Equal("GetCmsEntityById", entityByIdGet.GetProperty("operationId").GetString());
        Assert.Equal(
            "SetCmsEntityAdministrativeState",
            administrativePut.GetProperty("operationId").GetString());

        AssertExactResponseCodes(cmsPost, "200", "400", "401", "413", "415", "500", "503");
        AssertExactResponseCodes(entitiesGet, "200", "400", "401", "500");
        AssertExactResponseCodes(entityByIdGet, "200", "401", "404", "500");
        AssertExactResponseCodes(administrativePut, "200", "400", "401", "403", "404", "500", "503");

        var webhookRequestBody = cmsPost.GetProperty("requestBody");
        var webhookRequestContent = webhookRequestBody.GetProperty("content");
        Assert.True(webhookRequestContent.TryGetProperty("application/json", out var webhookApplicationJson));
        Assert.True(
            webhookRequestContent.TryGetProperty("application/*+json", out _),
            "Webhook operation does not document application/*+json.");

        var webhookRequestSchema = ResolveSchema(root, webhookApplicationJson.GetProperty("schema"));
        Assert.Equal("array", webhookRequestSchema.GetProperty("type").GetString());
        Assert.Equal(1, webhookRequestSchema.GetProperty("minItems").GetInt32());
        Assert.Equal(50, webhookRequestSchema.GetProperty("maxItems").GetInt32());

        var webhookItemSchema = ResolveSchema(root, webhookRequestSchema.GetProperty("items"));
        Assert.Equal("object", webhookItemSchema.GetProperty("type").GetString());
        AssertExactPropertySet(
            webhookItemSchema,
            ["eventId", "type", "id", "version", "timestamp", "payload"],
            "Webhook item schema");
        AssertSchemaDoesNotContainProperties(
            webhookItemSchema,
            [
                "entityId",
                "events",
                "payloadHash",
                "rowVersion",
                "generation",
                "latestVersion",
                "currentVersionOccurredAtUtc",
                "entityEventHighWatermarkUtc",
                "administrativeDisabled",
                "cmsPublicationStatus",
            ],
            "Webhook item schema");
        AssertExactRequiredSet(webhookItemSchema, ["type", "id", "timestamp"]);

        var webhookVersionProperty = ResolveSchema(
            root,
            webhookItemSchema.GetProperty("properties").GetProperty("version"));
        Assert.Equal("integer", webhookVersionProperty.GetProperty("type").GetString());
        Assert.Equal("int64", webhookVersionProperty.GetProperty("format").GetString());
        AssertNumericElementEqualsOne(webhookVersionProperty.GetProperty("minimum"));

        var webhookPayloadProperty = ResolveSchema(
            root,
            webhookItemSchema.GetProperty("properties").GetProperty("payload"));
        Assert.Equal("object", webhookPayloadProperty.GetProperty("type").GetString());

        var webhookTimestampProperty = ResolveSchema(
            root,
            webhookItemSchema.GetProperty("properties").GetProperty("timestamp"));
        Assert.Equal("string", webhookTimestampProperty.GetProperty("type").GetString());
        Assert.Equal("date-time", webhookTimestampProperty.GetProperty("format").GetString());

        var webhookExample = webhookApplicationJson.GetProperty("example");
        Assert.Equal(JsonValueKind.Array, webhookExample.ValueKind);
        Assert.Equal(3, webhookExample.GetArrayLength());

        var webhookExampleItems = webhookExample.EnumerateArray().ToArray();
        Assert.Contains(
            webhookExampleItems,
            item => string.Equals(item.GetProperty("type").GetString(), "Publish", StringComparison.Ordinal));
        Assert.Contains(
            webhookExampleItems,
            item => string.Equals(item.GetProperty("type").GetString(), "  unPublish  ", StringComparison.Ordinal));
        Assert.Contains(
            webhookExampleItems,
            item => string.Equals(item.GetProperty("type").GetString(), "Delete", StringComparison.Ordinal));
        Assert.Contains(webhookExampleItems, item => item.TryGetProperty("eventId", out _));
        Assert.Contains(webhookExampleItems, item => !item.TryGetProperty("eventId", out _));

        var deleteExample = webhookExampleItems.Single(item =>
            string.Equals(item.GetProperty("type").GetString(), "Delete", StringComparison.Ordinal));
        Assert.False(deleteExample.TryGetProperty("version", out _));
        Assert.False(deleteExample.TryGetProperty("payload", out _));
        Assert.All(webhookExampleItems, item => Assert.False(item.TryGetProperty("events", out _)));
        AssertNoCredentialLikeValues(webhookExample.GetRawText());

        var administrativeRequestBody = administrativePut.GetProperty("requestBody");
        var administrativeRequestJson = administrativeRequestBody.GetProperty("content").GetProperty("application/json");
        var administrativeRequestSchema = ResolveSchema(
            root,
            administrativeRequestJson.GetProperty("schema"));
        Assert.Equal("object", administrativeRequestSchema.GetProperty("type").GetString());
        AssertExactPropertySet(administrativeRequestSchema, ["Disabled"], "Administrative-state request schema");
        AssertExactRequiredSet(administrativeRequestSchema, ["Disabled"]);

        var administrativeRequestProperties = administrativeRequestSchema.GetProperty("properties");
        var disabledProperty = ResolveSchema(root, administrativeRequestProperties.GetProperty("Disabled"));
        Assert.Equal("boolean", disabledProperty.GetProperty("type").GetString());
        Assert.False(administrativeRequestProperties.TryGetProperty("disabled", out _));

        var administrativeRequestExample = administrativeRequestJson.GetProperty("example");
        Assert.Equal(JsonValueKind.Object, administrativeRequestExample.ValueKind);
        Assert.Single(administrativeRequestExample.EnumerateObject());
        Assert.True(administrativeRequestExample.TryGetProperty("Disabled", out _));
        Assert.False(administrativeRequestExample.TryGetProperty("disabled", out _));

        var webhookSuccessSchema = GetResponseSchema(root, cmsPost, "200", "application/json");
        AssertExactPropertySet(
            webhookSuccessSchema,
            ["batchId", "results", "summary"],
            "Webhook 200 response schema");
        AssertSchemaUsesCamelCasePropertyNames(webhookSuccessSchema, "Webhook 200 response schema");

        var webhookResultsSchema = ResolveSchema(
            root,
            webhookSuccessSchema.GetProperty("properties").GetProperty("results"));
        Assert.Equal("array", webhookResultsSchema.GetProperty("type").GetString());

        var webhookResultItemSchema = ResolveSchema(root, webhookResultsSchema.GetProperty("items"));
        AssertExactPropertySet(
            webhookResultItemSchema,
            ["sequence", "eventId", "id", "outcome", "code", "generation", "resultingVersion"],
            "Webhook 200 result item schema");
        AssertSchemaUsesCamelCasePropertyNames(webhookResultItemSchema, "Webhook 200 result item schema");

        var webhookSummarySchema = ResolveSchema(
            root,
            webhookSuccessSchema.GetProperty("properties").GetProperty("summary"));
        AssertExactPropertySet(
            webhookSummarySchema,
            ["total", "applied", "duplicate", "equivalent", "stale", "invalid", "conflict"],
            "Webhook 200 summary schema");
        AssertSchemaUsesCamelCasePropertyNames(webhookSummarySchema, "Webhook 200 summary schema");

        var entitiesListSchema = GetResponseSchema(root, entitiesGet, "200", "application/json");
        AssertExactPropertySet(
            entitiesListSchema,
            ["items", "pageSize", "nextCursor"],
            "Entity list 200 response schema");

        var listItemsSchema = ResolveSchema(root, entitiesListSchema.GetProperty("properties").GetProperty("items"));
        Assert.Equal("array", listItemsSchema.GetProperty("type").GetString());

        var entityListItemSchema = ResolveSchema(root, listItemsSchema.GetProperty("items"));
        var entityDetailSchema = GetResponseSchema(root, entityByIdGet, "200", "application/json");
        var expectedEntityResponseProperties = new[]
        {
            "id",
            "generation",
            "latestVersion",
            "payload",
            "cmsPublicationStatus",
            "currentVersionOccurredAtUtc",
            "entityEventHighWatermarkUtc",
            "administrativeDisabled",
        };

        AssertExactPropertySet(entityListItemSchema, expectedEntityResponseProperties, "Entity list item schema");
        AssertExactPropertySet(entityDetailSchema, expectedEntityResponseProperties, "Entity detail schema");
        AssertSchemaDoesNotContainProperties(
            entityListItemSchema,
            [
                "EntityId",
                "Generation",
                "LatestVersion",
                "Payload",
                "CmsPublicationStatus",
                "CurrentVersionOccurredAtUtc",
                "EntityEventHighWatermarkUtc",
                "AdministrativeDisabled",
            ],
            "Entity list item schema");
        AssertSchemaDoesNotContainProperties(
            entityDetailSchema,
            [
                "EntityId",
                "Generation",
                "LatestVersion",
                "Payload",
                "CmsPublicationStatus",
                "CurrentVersionOccurredAtUtc",
                "EntityEventHighWatermarkUtc",
                "AdministrativeDisabled",
            ],
            "Entity detail schema");

        var administrativeStateResponseSchema = GetResponseSchema(root, administrativePut, "200", "application/json");
        AssertExactPropertySet(
            administrativeStateResponseSchema,
            [
                "id",
                "administrativeDisabled",
                "administrativeStateChangedAtUtc",
                "administrativeStateChangedBy",
            ],
            "Administrative-state 200 response schema");
        AssertSchemaDoesNotContainProperties(
            administrativeStateResponseSchema,
            [
                "EntityId",
                "AdministrativeDisabled",
                "AdministrativeStateChangedAtUtc",
                "AdministrativeStateChangedBy",
            ],
            "Administrative-state 200 response schema");

        Assert.True(
            GetPropertyNames(entityDetailSchema).Contains("currentVersionOccurredAtUtc", StringComparer.Ordinal),
            "Entity detail schema is missing currentVersionOccurredAtUtc.");
        Assert.True(
            GetPropertyNames(entityDetailSchema).Contains("entityEventHighWatermarkUtc", StringComparer.Ordinal),
            "Entity detail schema is missing entityEventHighWatermarkUtc.");

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

        Assert.All(
            allBusinessOperations,
            operation => Assert.True(
                GetRequiredSecuritySchemes(operation).Length == 1,
                "An operation unexpectedly requires multiple security schemes."));

        AssertNoCredentialLikeValues(documentJson);
    }

    private static void AssertExactResponseCodes(JsonElement operation, params string[] expectedStatusCodes)
    {
        var responses = operation.GetProperty("responses");
        var expected = expectedStatusCodes
            .Distinct(StringComparer.Ordinal)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToArray();
        var actual = responses.EnumerateObject()
            .Select(response => response.Name)
            .Where(statusCode => !string.Equals(statusCode, "default", StringComparison.Ordinal))
            .OrderBy(statusCode => statusCode, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static JsonElement GetResponseSchema(
        JsonElement root,
        JsonElement operation,
        string statusCode,
        string mediaType)
    {
        var responses = operation.GetProperty("responses");
        Assert.True(
            responses.TryGetProperty(statusCode, out var response),
            $"Missing expected response status code: {statusCode}");

        var content = response.GetProperty("content");
        Assert.True(
            content.TryGetProperty(mediaType, out var responseMediaType),
            $"Missing expected response media type '{mediaType}' for status {statusCode}.");

        return ResolveSchema(root, responseMediaType.GetProperty("schema"));
    }

    private static void AssertExactPropertySet(
        JsonElement schema,
        IEnumerable<string> expectedPropertyNames,
        string schemaLabel)
    {
        var expected = expectedPropertyNames
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var actual = GetPropertyNames(schema);

        Assert.Equal(expected, actual);
    }

    private static string[] GetPropertyNames(JsonElement schema)
    {
        return schema.GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AssertSchemaDoesNotContainProperties(
        JsonElement schema,
        IEnumerable<string> forbiddenPropertyNames,
        string schemaLabel)
    {
        var properties = schema.GetProperty("properties");

        foreach (var forbiddenPropertyName in forbiddenPropertyNames)
        {
            Assert.False(
                properties.TryGetProperty(forbiddenPropertyName, out _),
                $"{schemaLabel} unexpectedly documents '{forbiddenPropertyName}'.");
        }
    }

    private static void AssertExactRequiredSet(JsonElement schema, IEnumerable<string> expectedRequiredProperties)
    {
        var expected = expectedRequiredProperties
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var actual = schema.TryGetProperty("required", out var requiredElement)
            ? requiredElement.EnumerateArray()
                .Select(entry => entry.GetString())
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Cast<string>()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
            : [];

        Assert.Equal(expected, actual);
    }

    private static void AssertNumericElementEqualsOne(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            Assert.Equal(1, element.GetInt64());
            return;
        }

        Assert.Equal(JsonValueKind.String, element.ValueKind);
        Assert.Equal(1.ToString(CultureInfo.InvariantCulture), element.GetString());
    }

    private static void AssertSchemaUsesCamelCasePropertyNames(JsonElement schema, string schemaLabel)
    {
        foreach (var propertyName in GetPropertyNames(schema))
        {
            Assert.True(
                propertyName.Length > 0 && char.IsLower(propertyName[0]),
                $"{schemaLabel} property '{propertyName}' is not camelCase.");
        }
    }

    private static void AssertNoCredentialLikeValues(string json)
    {
        Assert.DoesNotContain("Authorization: Basic", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"authorization\"", json, StringComparison.OrdinalIgnoreCase);

        Assert.False(
            CmsTestUsernamePattern.IsMatch(json),
            "OpenAPI document unexpectedly contains a CMS test username value.");
        Assert.False(
            ConsumerTestUsernamePattern.IsMatch(json),
            "OpenAPI document unexpectedly contains a consumer test username value.");
        Assert.False(
            AdministratorTestUsernamePattern.IsMatch(json),
            "OpenAPI document unexpectedly contains an administrator test username value.");
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

    private static string[] GetOperationTags(JsonElement operation)
    {
        if (!operation.TryGetProperty("tags", out var tags))
        {
            return [];
        }

        var values = tags
            .EnumerateArray()
            .Select(tag => tag.GetString())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Cast<string>()
            .ToArray();

        Assert.Single(values);
        return values;
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
