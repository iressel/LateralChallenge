namespace CmsSync.Application.EntityQueries;

public sealed record CmsEntityListQuery(
    int PageSize,
    string? AfterEntityId,
    CmsEntityQueryVisibility Visibility);
