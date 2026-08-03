using CmsSync.Infrastructure.Authentication;

namespace CmsSync.IntegrationTests.TestHost;

public sealed class TestCredentialSet
{
    private TestCredentialSet(
        string cmsUsername,
        string cmsPassword,
        string consumerUsername,
        string consumerPassword,
        string administratorUsername,
        string administratorPassword)
    {
        CmsUsername = cmsUsername;
        CmsPassword = cmsPassword;
        ConsumerUsername = consumerUsername;
        ConsumerPassword = consumerPassword;
        AdministratorUsername = administratorUsername;
        AdministratorPassword = administratorPassword;
    }

    public string CmsUsername { get; }

    public string CmsPassword { get; }

    public string ConsumerUsername { get; }

    public string ConsumerPassword { get; }

    public string AdministratorUsername { get; }

    public string AdministratorPassword { get; }

    public static TestCredentialSet Create()
    {
        var cmsUsername = $"cms-{Guid.NewGuid():N}"[..16];
        var consumerUsername = $"consumer-{Guid.NewGuid():N}";
        var administratorUsername = $"administrator-{Guid.NewGuid():N}";
        var passwords = CreateDistinctPasswords();

        return new TestCredentialSet(
            cmsUsername,
            passwords[0],
            consumerUsername,
            passwords[1],
            administratorUsername,
            passwords[2]);
    }

    public Dictionary<string, string?> CreateConfiguration()
    {
        return new Dictionary<string, string?>
        {
            [$"{AuthenticationConstants.CredentialSection}:Cms:Username"] = CmsUsername,
            [$"{AuthenticationConstants.CredentialSection}:Cms:Password"] = CmsPassword,
            [$"{AuthenticationConstants.CredentialSection}:Consumer:Username"] = ConsumerUsername,
            [$"{AuthenticationConstants.CredentialSection}:Consumer:Password"] = ConsumerPassword,
            [$"{AuthenticationConstants.CredentialSection}:Administrator:Username"] = AdministratorUsername,
            [$"{AuthenticationConstants.CredentialSection}:Administrator:Password"] = AdministratorPassword,
        };
    }

    private static string[] CreateDistinctPasswords()
    {
        var passwords = new HashSet<string>(StringComparer.Ordinal);

        while (passwords.Count < 3)
        {
            var candidate = Guid.NewGuid().ToString("D");

            if (candidate.Any(character => character is >= 'a' and <= 'f'))
            {
                passwords.Add(candidate);
            }
        }

        return passwords.ToArray();
    }
}
