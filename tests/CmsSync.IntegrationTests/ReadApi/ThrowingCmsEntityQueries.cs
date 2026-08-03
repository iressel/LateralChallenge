using CmsSync.Application.Abstractions;
using CmsSync.Application.EntityQueries;

namespace CmsSync.IntegrationTests.ReadApi;

internal sealed class ThrowingCmsEntityQueries : ICmsEntityQueries
{
    private const string UnsafeDiagnostic =
        "SQL Server failed at table CmsEntities; Password=<non-secret-test-sentinel>; stack trace sentinel.";

    public Task<CmsEntityReadPage> ListAsync(
        CmsEntityListQuery query,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(UnsafeDiagnostic);
    }

    public Task<CmsEntityReadProjection?> FindByIdAsync(
        CmsEntityDetailQuery query,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(UnsafeDiagnostic);
    }
}
