namespace CmsSync.Application.EntityQueries;

public sealed record CmsEntityReadProjection(
    string EntityId,
    long Generation,
    long LatestVersion,
    string Payload,
    string CmsPublicationStatus,
    DateTime CurrentVersionOccurredAtUtc,
    DateTime EntityEventHighWatermarkUtc,
    bool AdministrativeDisabled);
