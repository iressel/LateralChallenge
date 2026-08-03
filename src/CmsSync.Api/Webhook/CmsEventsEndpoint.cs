using System.Net.Http.Headers;
using System.Security.Claims;
using CmsSync.Api.Contracts.CmsEvents;
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
        CmsEventBatchService batchService)
    {
        if (!IsSupportedJsonMediaType(context.Request.ContentType))
        {
            return CmsWebhookProblemResponse.Create(
                context,
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported CMS event media type",
                "Content-Type must be application/json or an application/*+json media type.",
                CmsWebhookProblemCodes.UnsupportedMediaType);
        }

        try
        {
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

                return CmsWebhookProblemResponse.Create(
                    context,
                    statusCode,
                    "Invalid CMS event request",
                    failure.Message,
                    failure.Code);
            }

            var authenticatedSubject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("The authenticated CMS identity has no subject identifier.");
            var request = new CmsEventBatchRequest(
                Guid.NewGuid(),
                parseResult.Items,
                context.TraceIdentifier,
                authenticatedSubject);
            var result = await batchService.ProcessAsync(request, context.RequestAborted);
            return Results.Ok(new CmsEventBatchResponse(result));
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (EventProcessingDependencyUnavailableException)
        {
            return CmsWebhookProblemResponse.Create(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "CMS event processing is temporarily unavailable",
                "A required dependency could not complete the CMS event batch.",
                CmsWebhookProblemCodes.DependencyUnavailable);
        }
        catch (Exception)
        {
            return CmsWebhookProblemResponse.Create(
                context,
                StatusCodes.Status500InternalServerError,
                "CMS event processing failed",
                "The CMS event batch could not be completed.",
                CmsWebhookProblemCodes.UnexpectedProcessingFailure);
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
