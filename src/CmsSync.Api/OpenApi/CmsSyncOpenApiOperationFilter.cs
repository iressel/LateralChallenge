using System.Globalization;
using System.Text.Json.Nodes;
using CmsSync.Api.Contracts.Entities;
using CmsSync.Api.Entities;
using CmsSync.Api.Webhook;
using CmsSync.Application.EntityQueries;
using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CmsSync.Api.OpenApi;

public sealed class CmsSyncOpenApiOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        ApplySecurityRequirement(operation, context);

        var normalizedPath = NormalizePath(context.ApiDescription.RelativePath);
        var method = context.ApiDescription.HttpMethod?.ToUpperInvariant();

        if (string.Equals(normalizedPath, CmsEventsRoutes.Route, StringComparison.Ordinal) &&
            string.Equals(method, HttpMethods.Post, StringComparison.Ordinal))
        {
            ApplyWebhookOperation(operation);
            return;
        }

        if (string.Equals(normalizedPath, CmsEntitiesRoutes.RoutePrefix, StringComparison.Ordinal) &&
            string.Equals(method, HttpMethods.Get, StringComparison.Ordinal))
        {
            ApplyEntityListOperation(operation);
            return;
        }

        if (string.Equals(
                normalizedPath,
                CmsEntitiesRoutes.RoutePrefix + "/{entityId}",
                StringComparison.Ordinal) &&
            string.Equals(method, HttpMethods.Get, StringComparison.Ordinal))
        {
            ApplyEntityDetailOperation(operation);
            return;
        }

        if (string.Equals(
                normalizedPath,
                CmsEntitiesRoutes.RoutePrefix + "/{entityId}" + CmsEntitiesRoutes.AdministrativeStateSuffix,
                StringComparison.Ordinal) &&
            string.Equals(method, HttpMethods.Put, StringComparison.Ordinal))
        {
            ApplyAdministrativeStateOperation(operation, context);
        }
    }

    private static void ApplyWebhookOperation(OpenApiOperation operation)
    {
        operation.Summary = "Process CMS event batch";
        operation.Description =
            "Accepts a raw JSON array of 1 through 50 CMS events. " +
            "On 500/503, retry the entire original request because earlier items may already be committed.";

        var webhookArraySchema = CreateWebhookArraySchema();
        var webhookExample = CreateWebhookArrayExample();
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Raw top-level JSON array; no wrapper object.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = webhookArraySchema,
                    Example = webhookExample,
                },
                ["application/*+json"] = new OpenApiMediaType
                {
                    Schema = webhookArraySchema,
                    Example = webhookExample,
                },
            },
        };
    }

    private static void ApplyEntityListOperation(OpenApiOperation operation)
    {
        operation.Summary = "List CMS entities";
        operation.Description =
            "Returns a role-scoped page of entities ordered by case-sensitive identifier. " +
            "Duplicate pageSize query values are invalid.";

        operation.Parameters ??= new List<IOpenApiParameter>();
        UpsertParameter(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = "pageSize",
                In = ParameterLocation.Query,
                Required = false,
                Description =
                    "Optional page size. Valid values are 1 through 100, default is 20. Duplicate pageSize query values are invalid.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Minimum = CmsEntityQueryLimits.MinimumPageSize.ToString(CultureInfo.InvariantCulture),
                    Maximum = CmsEntityQueryLimits.MaximumPageSize.ToString(CultureInfo.InvariantCulture),
                    Default = JsonValue.Create(CmsEntityQueryLimits.DefaultPageSize),
                },
            });
        UpsertParameter(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = "afterEntityId",
                In = ParameterLocation.Query,
                Required = false,
                Description = "Optional opaque cursor. Comparison and ordering are case-sensitive.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                },
            });
    }

    private static void ApplyEntityDetailOperation(OpenApiOperation operation)
    {
        operation.Summary = "Get CMS entity by identifier";
        operation.Description =
            "Returns one role-visible entity. Hidden, deleted, and unknown entities are reported using the same non-disclosing 404 behavior.";

        operation.Parameters ??= new List<IOpenApiParameter>();
        UpsertParameter(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = "entityId",
                In = ParameterLocation.Path,
                Required = true,
                Description = "Case-sensitive entity identifier.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                },
            });
    }

    private static void ApplyAdministrativeStateOperation(
        OpenApiOperation operation,
        OperationFilterContext context)
    {
        operation.Summary = "Set local administrative visibility state";
        operation.Description =
            "Requires ConsumerBasic authentication with the Administrator role. " +
            "A normal consumer receives 403 without a challenge. " +
            "The request property name is case-sensitive and must be Disabled.";

        operation.Parameters ??= new List<IOpenApiParameter>();
        UpsertParameter(
            operation.Parameters,
            new OpenApiParameter
            {
                Name = "entityId",
                In = ParameterLocation.Path,
                Required = true,
                Description = "Case-sensitive entity identifier.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                },
            });

        var schema = context.SchemaGenerator.GenerateSchema(
            typeof(CmsAdministrativeStateRequest),
            context.SchemaRepository);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "JSON object with exact-case boolean property Disabled.",
            Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = schema,
                    Example = JsonNode.Parse("{\"Disabled\":true}"),
                },
            },
        };
    }

    private static OpenApiSchema CreateWebhookArraySchema()
    {
        return new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            MinItems = 1,
            MaxItems = 50,
            Items = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Description =
                    "eventId is optional. type accepts Publish, Unpublish, and Delete after trimming and case-insensitive normalization. " +
                    "id and timestamp are required. version and payload are required for Publish/Unpublish and prohibited for Delete.",
                Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal)
                {
                    ["eventId"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Optional external event identifier.",
                    },
                    ["type"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Publish, Unpublish, or Delete. Matching is trim-aware and case-insensitive.",
                    },
                    ["id"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Description = "Required external entity identifier.",
                    },
                    ["version"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Integer,
                        Format = "int64",
                        Minimum = "1",
                        Description = "Required for Publish/Unpublish; prohibited for Delete.",
                    },
                    ["timestamp"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "date-time",
                        Description = "Required CMS event timestamp.",
                    },
                    ["payload"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        AdditionalPropertiesAllowed = true,
                        AdditionalProperties = new OpenApiSchema(),
                        Description = "Required JSON object for Publish/Unpublish; prohibited for Delete.",
                    },
                },
                Required = new HashSet<string>(StringComparer.Ordinal)
                {
                    "type",
                    "id",
                    "timestamp",
                },
            },
        };
    }

    private static JsonNode CreateWebhookArrayExample()
    {
        return JsonNode.Parse(
                """
                        [
                            {
                                "eventId": "evt-1001",
                                "type": "Publish",
                                "id": "entity-openapi-01",
                                "version": 7,
                                "timestamp": "2026-08-05T14:30:00Z",
                                "payload": {
                                    "source": "openapi-example",
                                    "value": 7
                                }
                            },
                            {
                                "type": "  unPublish  ",
                                "id": "entity-openapi-01",
                                "version": 8,
                                "timestamp": "2026-08-05T14:29:30Z",
                                "payload": {
                                    "source": "openapi-example",
                                    "value": 8
                                }
                            },
                            {
                                "eventId": "evt-1003",
                                "type": "Delete",
                                "id": "entity-openapi-01",
                                "timestamp": "2026-08-05T14:31:00Z"
                            }
                        ]
                        """) ?? throw new InvalidOperationException("Webhook OpenAPI example could not be created.");
    }

    private static void ApplySecurityRequirement(
        OpenApiOperation operation,
        OperationFilterContext context)
    {
        var endpointMetadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        if (endpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            operation.Security = new List<OpenApiSecurityRequirement>();
            return;
        }

        var authorizeData = endpointMetadata.OfType<IAuthorizeData>().ToArray();

        if (authorizeData.Length == 0)
        {
            return;
        }

        var policies = authorizeData
            .Select(data => data.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .ToHashSet(StringComparer.Ordinal);
        var requiresCms = policies.Contains(AuthenticationConstants.CmsEventsPolicy);
        var requiresConsumer = policies.Contains(AuthenticationConstants.ConsumerAccessPolicy) ||
                               policies.Contains(AuthenticationConstants.AdministratorAccessPolicy);

        string? schemeName = null;

        if (requiresCms)
        {
            schemeName = AuthenticationConstants.CmsScheme;
        }

        if (requiresConsumer)
        {
            schemeName = AuthenticationConstants.ConsumerScheme;
        }

        if (schemeName is null)
        {
            return;
        }

        operation.Security = new List<OpenApiSecurityRequirement>
        {
            CreateSecurityRequirement(schemeName, context),
        };
    }

    private static OpenApiSecurityRequirement CreateSecurityRequirement(
        string schemeName,
        OperationFilterContext context)
    {
        var securitySchemeReference = new OpenApiSecuritySchemeReference(schemeName, context.Document);

        return new OpenApiSecurityRequirement
        {
            [securitySchemeReference] = [],
        };
    }

    private static string NormalizePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return "/";
        }

        var path = relativePath;
        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);

        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path[..^1];
        }

        return path;
    }

    private static void UpsertParameter(
        IList<IOpenApiParameter> parameters,
        OpenApiParameter parameter)
    {
        for (var index = 0; index < parameters.Count; index++)
        {
            if (string.Equals(parameters[index].Name, parameter.Name, StringComparison.Ordinal) &&
                parameters[index].In == parameter.In)
            {
                parameters[index] = parameter;
                return;
            }
        }

        parameters.Add(parameter);
    }
}
