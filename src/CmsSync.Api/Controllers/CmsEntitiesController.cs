using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CmsSync.Api.Contracts.Entities;
using CmsSync.Api.Entities;
using CmsSync.Api.Errors;
using CmsSync.Application.Abstractions;
using CmsSync.Application.AdministrativeState;
using CmsSync.Application.EntityQueries;
using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CmsSync.Api.Controllers;

[ApiController]
[Route(CmsEntitiesRoutes.RouteTemplate)]
[Authorize(Policy = AuthenticationConstants.ConsumerAccessPolicy)]
public sealed class CmsEntitiesController : ControllerBase
{
    private static readonly JsonSerializerOptions AdministrativeStateRequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly ICmsEntityQueries _entityQueries;
    private readonly IAdministrativeStateService _administrativeStateService;

    public CmsEntitiesController(
        ICmsEntityQueries entityQueries,
        IAdministrativeStateService administrativeStateService)
    {
        _entityQueries = entityQueries ?? throw new ArgumentNullException(nameof(entityQueries));
        _administrativeStateService = administrativeStateService
            ?? throw new ArgumentNullException(nameof(administrativeStateService));
    }

    [HttpGet]
    [ProducesResponseType(typeof(CmsEntityListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IResult> ListEntitiesAsync()
    {
        var context = HttpContext;

        if (!TryReadPageSize(context.Request, out var pageSize))
        {
            return SafeProblemDetails.Create(
                context,
                StatusCodes.Status400BadRequest,
                "Invalid entity list request",
                "pageSize must be an integer from 1 through 100.",
                CmsEntityProblemCodes.InvalidPageSize);
        }

        var query = new CmsEntityListQuery(
            pageSize,
            ReadAfterEntityId(context.Request),
            ResolveVisibility(context));
        var page = await _entityQueries.ListAsync(query, context.RequestAborted);
        var responseItems = page.Items.Select(CreateResponse).ToArray();

        return Results.Ok(new CmsEntityListResponse(
            responseItems,
            pageSize,
            page.NextCursor));
    }

    [HttpGet("{entityId}")]
    [ProducesResponseType(typeof(CmsEntityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IResult> GetEntityByIdAsync(string entityId)
    {
        var context = HttpContext;
        var query = new CmsEntityDetailQuery(entityId, ResolveVisibility(context));
        var entity = await _entityQueries.FindByIdAsync(query, context.RequestAborted);

        if (entity is null)
        {
            return SafeProblemDetails.Create(
                context,
                StatusCodes.Status404NotFound,
                "Entity not found",
                "The requested entity was not found.",
                CmsEntityProblemCodes.EntityNotFound);
        }

        return Results.Ok(CreateResponse(entity));
    }

    [HttpPut("{entityId}/administrative-state")]
    [Authorize(Policy = AuthenticationConstants.AdministratorAccessPolicy)]
    [ProducesResponseType(typeof(CmsAdministrativeStateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> SetAdministrativeStateAsync(string entityId)
    {
        var context = HttpContext;
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
            return CreateInvalidAdministrativeStateRequest(context);
        }

        if (request is null)
        {
            return CreateInvalidAdministrativeStateRequest(context);
        }

        var administratorSubject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(administratorSubject))
        {
            throw new InvalidOperationException("The authenticated administrator has no subject identifier.");
        }

        var result = await _administrativeStateService.SetAsync(
            entityId,
            request.Disabled,
            administratorSubject,
            context.RequestAborted);

        if (result is null)
        {
            return SafeProblemDetails.Create(
                context,
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

    private static IResult CreateInvalidAdministrativeStateRequest(HttpContext context)
    {
        return SafeProblemDetails.Create(
            context,
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
