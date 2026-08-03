using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace CmsSync.Infrastructure.Authentication;

public sealed class AuthenticationResponseSecurityMiddleware
{
    private readonly RequestDelegate _next;

    public AuthenticationResponseSecurityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(() =>
        {
            ProtectSecurityResponse(context.Response);
            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static void ProtectSecurityResponse(HttpResponse response)
    {
        if (response.StatusCode != StatusCodes.Status401Unauthorized &&
            response.StatusCode != StatusCodes.Status403Forbidden &&
            !IsHttpsRedirect(response))
        {
            return;
        }

        if (!response.Headers.CacheControl.ToString().Contains(
                "no-store",
                StringComparison.OrdinalIgnoreCase))
        {
            response.Headers.CacheControl = "no-store";
        }

        if (!response.Headers.Pragma.ToString().Contains(
                "no-cache",
                StringComparison.OrdinalIgnoreCase))
        {
            response.Headers.Pragma = "no-cache";
        }
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
