namespace CmsSync.Infrastructure.Authentication;

public sealed class CredentialOptions
{
    public CredentialIdentityOptions? Cms { get; set; }

    public CredentialIdentityOptions? Consumer { get; set; }

    public CredentialIdentityOptions? Administrator { get; set; }
}
