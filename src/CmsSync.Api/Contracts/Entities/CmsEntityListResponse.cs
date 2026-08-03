using System.Text.Json.Serialization;

namespace CmsSync.Api.Contracts.Entities;

public sealed record CmsEntityListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<CmsEntityResponse> Items,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("nextCursor")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NextCursor);
