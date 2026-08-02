namespace CmsSync.Application.EventIngestion;

public sealed class EventIdentity
{
    internal EventIdentity(string idempotencyKey, EventContentHash contentHash)
    {
        IdempotencyKey = idempotencyKey;
        ContentHash = contentHash;
    }

    public string IdempotencyKey { get; }

    public EventContentHash ContentHash { get; }

    public override string ToString()
    {
        return $"Namespace = {(IdempotencyKey.StartsWith(EventIdentityFactory.ExternalPrefix, StringComparison.Ordinal) ? "external" : "sha256")}, ContentHash = {ContentHash}";
    }
}
