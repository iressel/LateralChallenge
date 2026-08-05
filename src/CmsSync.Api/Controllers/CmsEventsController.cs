using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Claims;
using CmsSync.Api.Contracts.CmsEvents;
using CmsSync.Api.Errors;
using CmsSync.Api.Observability;
using CmsSync.Api.Webhook;
using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CmsSync.Api.Controllers;

[ApiController]
[Route(CmsEventsRoutes.RouteTemplate)]
[Authorize(Policy = AuthenticationConstants.CmsEventsPolicy)]
public sealed class CmsEventsController : ControllerBase
{
    private readonly CmsEventArrayParser _parser;
    private readonly CmsEventBatchService _batchService;
    private readonly CmsEventBatchTelemetry _telemetry;

    public CmsEventsController(
        CmsEventArrayParser parser,
        CmsEventBatchService batchService,
        CmsEventBatchTelemetry telemetry)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _batchService = batchService ?? throw new ArgumentNullException(nameof(batchService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CmsEventBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> ProcessEventsAsync()
    {
        var context = HttpContext;

        if (!IsSupportedJsonMediaType(context.Request.ContentType))
        {
            return SafeProblemDetails.Create(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported CMS event media type",
                "Content-Type must be application/json or an application/*+json media type.",
                CmsWebhookProblemCodes.UnsupportedMediaType);
        }

        var requestBody = context.Features.Get<CmsWebhookRequestBody>()
            ?? throw new InvalidOperationException("The CMS webhook request body was not size-validated.");
        var parseResult = _parser.Parse(requestBody.Utf8Json);

        if (!parseResult.IsSuccess)
        {
            var failure = parseResult.Failure
                ?? throw new InvalidOperationException("An unsuccessful CMS event parse has no failure.");
            var statusCode = failure.Code == CmsEventParsingCodes.RequestTooLarge
                ? StatusCodes.Status413PayloadTooLarge
                : StatusCodes.Status400BadRequest;

            return SafeProblemDetails.Create(
                context,
                statusCode,
                "Invalid CMS event request",
                failure.Message,
                failure.Code);
        }

        var authenticatedSubject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("The authenticated CMS identity has no subject identifier.");
        var batchId = Guid.NewGuid();
        var correlationId = CorrelationContextAccessor.GetCorrelationId(context);
        var request = new CmsEventBatchRequest(
            batchId,
            parseResult.Items,
            correlationId,
            authenticatedSubject);
        var traceId = Activity.Current?.TraceId.ToString() ?? "none";
        var startedTimestamp = Stopwatch.GetTimestamp();
        _telemetry.RecordStarted(batchId, parseResult.Items.Count, correlationId, traceId);

        try
        {
            var result = await _batchService.ProcessAsync(request, context.RequestAborted);
            _telemetry.RecordCompleted(
                batchId,
                parseResult.Items.Count,
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                correlationId,
                traceId);

            return Results.Ok(new CmsEventBatchResponse(result));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (EventProcessingDependencyUnavailableException)
        {
            _telemetry.RecordFailed(
                batchId,
                parseResult.Items.Count,
                "dependency_unavailable",
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                correlationId);
            throw;
        }
        catch (Exception)
        {
            _telemetry.RecordFailed(
                batchId,
                parseResult.Items.Count,
                "unexpected_failure",
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                correlationId);
            throw;
        }
    }

    private static bool IsSupportedJsonMediaType(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsedContentType))
        {
            return false;
        }

        var mediaType = parsedContentType.MediaType;

        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        const string applicationPrefix = "application/";
        const string jsonSuffix = "+json";

        return mediaType is not null &&
               mediaType.StartsWith(applicationPrefix, StringComparison.OrdinalIgnoreCase) &&
               mediaType.EndsWith(jsonSuffix, StringComparison.OrdinalIgnoreCase) &&
               mediaType.Length > applicationPrefix.Length + jsonSuffix.Length;
    }
}
