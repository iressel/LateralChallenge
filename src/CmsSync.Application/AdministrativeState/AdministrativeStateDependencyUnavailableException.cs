namespace CmsSync.Application.AdministrativeState;

public sealed class AdministrativeStateDependencyUnavailableException : Exception
{
    public AdministrativeStateDependencyUnavailableException()
        : base("The administrative state dependency is unavailable.")
    {
    }
}
