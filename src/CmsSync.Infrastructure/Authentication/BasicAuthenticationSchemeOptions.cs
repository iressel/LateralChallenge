using Microsoft.AspNetCore.Authentication;

namespace CmsSync.Infrastructure.Authentication;

public sealed class BasicAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public string Realm { get; set; } = string.Empty;

    public CredentialAudience Audience { get; set; }
}
