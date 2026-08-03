using CmsSync.Application.AdministrativeState;

namespace CmsSync.IntegrationTests.AdministrativeState;

internal sealed class ThrowingAdministrativeStateService : IAdministrativeStateService
{
    public Task<AdministrativeStateResult?> SetAsync(
        string entityId,
        bool disabled,
        string administratorSubject,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(
            "SQL table CmsEntities and RowVersion stack trace leak-detection sentinel.");
    }
}
