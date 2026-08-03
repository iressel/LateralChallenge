using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Claims;
using CmsSync.Api.Contracts.CmsEvents;
using CmsSync.Api.Errors;
using CmsSync.Api.Observability;
using CmsSync.Application.EventIngestion;
using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace CmsSync.Api.Webhook;

public static class CmsEventsEndpoint
{
    public const string Route = "/cms/events";

    public static RouteHandlerBuilder MapCmsEvents(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapPost(Route, HandleAsync)
            .RequireAuthorization(AuthenticationConstants.CmsEventsPolicy)
            .Produces<CmsEventBatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        CmsEventArrayParser parser,
        CmsEventBatchService batchService,
        CmsEventBatchTelemetry telemetry)
    {
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
        var parseResult = parser.Parse(requestBody.Utf8Json);

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
        telemetry.RecordStarted(batchId, parseResult.Items.Count, correlationId, traceId);

        try
        {
            var result = await batchService.ProcessAsync(request, context.RequestAborted);
            telemetry.RecordCompleted(
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
            telemetry.RecordFailed(
                batchId,
                parseResult.Items.Count,
                "dependency_unavailable",
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                correlationId);
            throw;
        }
        catch (Exception)
        {
            telemetry.RecordFailed(
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
