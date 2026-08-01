using CmsSync.Application.EntityQueries;

namespace CmsSync.Application.Abstractions;

public interface ICmsEntityQueries
{
    Task<CmsEntityReadPage> ListAsync(
        CmsEntityListQuery query,
        CancellationToken cancellationToken);

    Task<CmsEntityReadProjection?> FindByIdAsync(
        CmsEntityDetailQuery query,
        CancellationToken cancellationToken);
}
