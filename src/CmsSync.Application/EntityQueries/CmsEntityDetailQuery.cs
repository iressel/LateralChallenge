namespace CmsSync.Application.EntityQueries;

public sealed record CmsEntityDetailQuery(
    string EntityId,
    CmsEntityQueryVisibility Visibility);
