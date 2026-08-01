namespace CmsSync.Application.EntityQueries;

public sealed record CmsEntityReadPage(
    IReadOnlyList<CmsEntityReadProjection> Items,
    string? NextCursor);
