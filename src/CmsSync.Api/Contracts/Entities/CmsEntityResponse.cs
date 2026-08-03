using System.Text.Json;
using System.Text.Json.Serialization;

namespace CmsSync.Api.Contracts.Entities;

public sealed record CmsEntityResponse(
    [property: JsonPropertyName("id")] string EntityId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("latestVersion")] long LatestVersion,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("cmsPublicationStatus")] string CmsPublicationStatus,
    [property: JsonPropertyName("currentVersionOccurredAtUtc")] DateTime CurrentVersionOccurredAtUtc,
    [property: JsonPropertyName("entityEventHighWatermarkUtc")] DateTime EntityEventHighWatermarkUtc,
    [property: JsonPropertyName("administrativeDisabled")] bool AdministrativeDisabled);
