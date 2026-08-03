namespace CmsSync.Infrastructure.Authentication;

public static class AuthenticationConstants
{
    public const string CredentialSection = "Authentication:Credentials";

    public const string CmsScheme = "CmsBasic";
    public const string ConsumerScheme = "ConsumerBasic";

    public const string CmsEventsPolicy = "CmsEvents";
    public const string ConsumerAccessPolicy = "ConsumerAccess";
    public const string AdministratorAccessPolicy = "AdministratorAccess";

    public const string CmsServiceRole = "CmsService";
    public const string NormalConsumerRole = "NormalConsumer";
    public const string AdministratorRole = "Administrator";

    public const int MinimumCmsUsernameLength = 10;
    public const int MaximumCmsUsernameLength = 20;
    public const int MaximumUsernameLength = 128;
    public const int MaximumUsernameByteLength = MaximumUsernameLength * 4;
    public const int MaximumSuppliedPasswordLength = 256;
    public const int MaximumSuppliedPasswordByteLength = MaximumSuppliedPasswordLength * 4;
    public const int MaximumDecodedCredentialByteLength =
        MaximumUsernameByteLength + 1 + MaximumSuppliedPasswordByteLength;
    public const int MaximumEncodedCredentialLength =
        ((MaximumDecodedCredentialByteLength + 2) / 3) * 4;
    public const int MaximumAuthorizationHeaderLength =
        MaximumEncodedCredentialLength + 6;
}
