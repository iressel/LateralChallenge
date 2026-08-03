using CmsSync.Api.Entities;
using CmsSync.Api.Health;
using Microsoft.Net.Http.Headers;

namespace CmsSync.Api.Security;

public sealed class SafeResponseHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SafeResponseHeadersMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            if (RequiresNoStore(context))
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static bool RequiresNoStore(HttpContext context)
    {
        return context.Response.StatusCode >= StatusCodes.Status400BadRequest ||
               context.Request.Path.StartsWithSegments(CmsEntitiesEndpoint.RoutePrefix) ||
               context.Request.Path.StartsWithSegments(HealthEndpointRoutes.Prefix) ||
               IsHttpsRedirect(context.Response);
    }

    private static bool IsHttpsRedirect(HttpResponse response)
    {
        if (response.StatusCode < StatusCodes.Status300MultipleChoices ||
            response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return false;
        }

        var location = response.Headers.Location.ToString();
        return Uri.TryCreate(location, UriKind.Absolute, out var redirectUri) &&
               string.Equals(redirectUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
