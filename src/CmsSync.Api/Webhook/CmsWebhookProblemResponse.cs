using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace CmsSync.Api.Webhook;

internal static class CmsWebhookProblemResponse
{
    public static IResult Create(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string code)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";

        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            });
    }

    public static Task WriteAsync(
        HttpContext context,
        int statusCode,
        string title,
        string detail,
        string code)
    {
        return Create(context, statusCode, title, detail, code).ExecuteAsync(context);
    }
}
