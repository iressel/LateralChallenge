namespace CmsSync.Api.Webhook;

internal sealed class CmsWebhookRequestBody
{
    public CmsWebhookRequestBody(byte[] utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        Utf8Json = utf8Json;
    }

    public ReadOnlyMemory<byte> Utf8Json { get; }
}
