using System.Net;

namespace CmsSync.IntegrationTests.ReadApi;

internal sealed record ReadApiPageResult(
    HttpStatusCode StatusCode,
    int PageSize,
    string[] Ids,
    string? NextCursor);
