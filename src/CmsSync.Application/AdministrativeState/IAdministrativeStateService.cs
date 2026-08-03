namespace CmsSync.Application.AdministrativeState;

public interface IAdministrativeStateService
{
    Task<AdministrativeStateResult?> SetAsync(
        string entityId,
        bool disabled,
        string administratorSubject,
        CancellationToken cancellationToken);
}
