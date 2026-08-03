using System.Text.Json.Serialization;

namespace CmsSync.Api.Contracts.Entities;

public sealed record CmsAdministrativeStateResponse(
    [property: JsonPropertyName("id")] string EntityId,
    [property: JsonPropertyName("administrativeDisabled")] bool AdministrativeDisabled,
    [property: JsonPropertyName("administrativeStateChangedAtUtc")]
    DateTime? AdministrativeStateChangedAtUtc,
    [property: JsonPropertyName("administrativeStateChangedBy")]
    string? AdministrativeStateChangedBy);
