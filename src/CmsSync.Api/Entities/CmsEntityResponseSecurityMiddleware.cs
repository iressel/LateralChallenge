using Microsoft.AspNetCore.Http;

namespace CmsSync.Api.Entities;

public sealed class CmsEntityResponseSecurityMiddleware
{
    private readonly RequestDelegate _next;

    public CmsEntityResponseSecurityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Request.Path.StartsWithSegments(CmsEntitiesEndpoint.RoutePrefix))
        {
            await _next(context);
            return;
        }

        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
