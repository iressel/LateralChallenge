using Microsoft.AspNetCore.Http;

namespace CmsSync.Api.Entities;

internal static class CmsEntityProblemResponse
{
    public static IResult Create(
        int statusCode,
        string title,
        string detail,
        string code)
    {
        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
            });
    }
}
