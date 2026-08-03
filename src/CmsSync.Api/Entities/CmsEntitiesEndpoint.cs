using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CmsSync.Api.Contracts.Entities;
using CmsSync.Application.Abstractions;
using CmsSync.Application.AdministrativeState;
using CmsSync.Application.EntityQueries;
using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace CmsSync.Api.Entities;

public static class CmsEntitiesEndpoint
{
    public const string RoutePrefix = "/api/entities";

    private static readonly JsonSerializerOptions AdministrativeStateRequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public static RouteGroupBuilder MapCmsEntities(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(RoutePrefix)
            .RequireAuthorization(AuthenticationConstants.ConsumerAccessPolicy);

        group.MapGet(string.Empty, ListAsync)
            .Produces<CmsEntityListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/{entityId}", FindByIdAsync)
            .Produces<CmsEntityResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPut("/{entityId}/administrative-state", SetAdministrativeStateAsync)
            .RequireAuthorization(AuthenticationConstants.AdministratorAccessPolicy)
            .Produces<CmsAdministrativeStateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ICmsEntityQueries entityQueries)
    {
        if (!TryReadPageSize(context.Request, out var pageSize))
        {
            return CmsEntityProblemResponse.Create(
                StatusCodes.Status400BadRequest,
                "Invalid entity list request",
                "pageSize must be an integer from 1 through 100.",
                CmsEntityProblemCodes.InvalidPageSize);
        }

        try
        {
            var query = new CmsEntityListQuery(
                pageSize,
                ReadAfterEntityId(context.Request),
                ResolveVisibility(context));
            var page = await entityQueries.ListAsync(query, context.RequestAborted);
            var responseItems = page.Items.Select(CreateResponse).ToArray();

            return Results.Ok(new CmsEntityListResponse(
                responseItems,
                pageSize,
                page.NextCursor));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CmsEntityProblemResponse.Create(
                StatusCodes.Status500InternalServerError,
                "Entity query failed",
                "The entity list could not be retrieved.",
                CmsEntityProblemCodes.QueryFailed);
        }
    }

    private static async Task<IResult> FindByIdAsync(
        string entityId,
        HttpContext context,
        ICmsEntityQueries entityQueries)
    {
        try
        {
            var query = new CmsEntityDetailQuery(entityId, ResolveVisibility(context));
            var entity = await entityQueries.FindByIdAsync(query, context.RequestAborted);

            if (entity is null)
            {
                return CmsEntityProblemResponse.Create(
                    StatusCodes.Status404NotFound,
                    "Entity not found",
                    "The requested entity was not found.",
                    CmsEntityProblemCodes.EntityNotFound);
            }

            return Results.Ok(CreateResponse(entity));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return CmsEntityProblemResponse.Create(
                StatusCodes.Status500InternalServerError,
                "Entity query failed",
                "The requested entity could not be retrieved.",
                CmsEntityProblemCodes.QueryFailed);
        }
    }

    private static bool TryReadPageSize(HttpRequest request, out int pageSize)
    {
        pageSize = CmsEntityQueryLimits.DefaultPageSize;

        if (!request.Query.TryGetValue("pageSize", out var suppliedValues))
        {
            return true;
        }

        return suppliedValues.Count == 1 &&
               int.TryParse(
                   suppliedValues[0],
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out pageSize) &&
               pageSize is >= CmsEntityQueryLimits.MinimumPageSize and <= CmsEntityQueryLimits.MaximumPageSize;
    }

    private static async Task<IResult> SetAdministrativeStateAsync(
        string entityId,
        HttpContext context,
        IAdministrativeStateService administrativeStateService)
    {
        CmsAdministrativeStateRequest? request;

        try
        {
            request = await JsonSerializer.DeserializeAsync<CmsAdministrativeStateRequest>(
                context.Request.Body,
                AdministrativeStateRequestJsonOptions,
                context.RequestAborted);
        }
        catch (JsonException)
        {
            return CreateInvalidAdministrativeStateRequest();
        }

        if (request is null)
        {
            return CreateInvalidAdministrativeStateRequest();
        }

        var administratorSubject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(administratorSubject))
        {
            return CmsEntityProblemResponse.Create(
                StatusCodes.Status500InternalServerError,
                "Administrative state update failed",
                "The administrative state could not be updated.",
                CmsEntityProblemCodes.AdministrativeStateUpdateFailed);
        }

        try
        {
            var result = await administrativeStateService.SetAsync(
                entityId,
                request.Disabled,
                administratorSubject,
                context.RequestAborted);

            if (result is null)
            {
                return CmsEntityProblemResponse.Create(
                    StatusCodes.Status404NotFound,
                    "Entity not found",
                    "The requested entity was not found.",
                    CmsEntityProblemCodes.EntityNotFound);
            }

            return Results.Ok(new CmsAdministrativeStateResponse(
                result.EntityId,
                result.AdministrativeDisabled,
                result.AdministrativeStateChangedAtUtc,
                result.AdministrativeStateChangedBy));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (AdministrativeStateDependencyUnavailableException)
        {
            return CmsEntityProblemResponse.Create(
                StatusCodes.Status503ServiceUnavailable,
                "Administrative state unavailable",
                "The administrative state could not be updated at this time.",
                CmsEntityProblemCodes.AdministrativeStateUnavailable);
        }
        catch (Exception)
        {
            return CmsEntityProblemResponse.Create(
                StatusCodes.Status500InternalServerError,
                "Administrative state update failed",
                "The administrative state could not be updated.",
                CmsEntityProblemCodes.AdministrativeStateUpdateFailed);
        }
    }

    private static IResult CreateInvalidAdministrativeStateRequest()
    {
        return CmsEntityProblemResponse.Create(
            StatusCodes.Status400BadRequest,
            "Invalid administrative state request",
            "Disabled must be provided as a boolean property with exact casing.",
            CmsEntityProblemCodes.InvalidAdministrativeStateRequest);
    }

    private static string? ReadAfterEntityId(HttpRequest request)
    {
        if (!request.Query.TryGetValue("afterEntityId", out var suppliedValues) ||
            suppliedValues.Count == 0)
        {
            return null;
        }

        return suppliedValues[0];
    }

    private static CmsEntityQueryVisibility ResolveVisibility(HttpContext context)
    {
        return context.User.IsInRole(AuthenticationConstants.AdministratorRole)
            ? CmsEntityQueryVisibility.Administrator
            : CmsEntityQueryVisibility.Consumer;
    }

    private static CmsEntityResponse CreateResponse(CmsEntityReadProjection entity)
    {
        using var payloadDocument = JsonDocument.Parse(entity.Payload);

        return new CmsEntityResponse(
            entity.EntityId,
            entity.Generation,
            entity.LatestVersion,
            payloadDocument.RootElement.Clone(),
            entity.CmsPublicationStatus,
            DateTime.SpecifyKind(entity.CurrentVersionOccurredAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(entity.EntityEventHighWatermarkUtc, DateTimeKind.Utc),
            entity.AdministrativeDisabled);
    }
}
