using System.Text.Json.Serialization;

namespace CmsSync.Api.Contracts.Entities;

public sealed record CmsAdministrativeStateRequest(
    [property: JsonPropertyName("Disabled")]
    [property: JsonRequired]
    bool Disabled);
