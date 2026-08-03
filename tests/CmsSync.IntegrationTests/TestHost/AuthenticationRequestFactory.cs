using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace CmsSync.IntegrationTests.TestHost;

public static class AuthenticationRequestFactory
{
    public static HttpRequestMessage CreateBasicGet(
        string requestUri,
        string username,
        string password,
        string scheme = "Basic")
    {
        var parameter = CreateBasicParameter(username, password);
        return CreateGet(requestUri, scheme, parameter);
    }

    public static HttpRequestMessage CreateGet(
        string requestUri,
        string scheme,
        string? parameter)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(scheme, parameter);
        return request;
    }

    public static string CreateBasicParameter(string username, string password)
    {
        var credentialBytes = Encoding.UTF8.GetBytes($"{username}:{password}");

        try
        {
            return Convert.ToBase64String(credentialBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(credentialBytes);
        }
    }
}
