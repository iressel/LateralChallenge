using CmsSync.Domain.Entities;

namespace CmsSync.Domain.Visibility;

public static class EntityVisibilityPolicy
{
    public static bool IsVisibleToNormalConsumer(ActiveCmsEntitySnapshot? entity)
    {
        return entity is
        {
            PublicationStatus: CmsPublicationStatus.Published,
            AdministrativeDisabled: false,
        };
    }

    public static bool IsVisibleToAdministrator(ActiveCmsEntitySnapshot? entity)
    {
        return entity is not null;
    }
}
