using System.Text;
using CmsSync.Application.EventIngestion;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

internal static class EventIngestionTestHelper
{
    public const string Timestamp = "2026-07-31T12:34:56.1234567Z";

    public static ParsedCmsEventItem ParseSingle(
        string eventJson,
        CmsEventIngestionLimits? limits = null)
    {
        var parser = new CmsEventArrayParser(limits);
        var parseResult = parser.Parse(Encoding.UTF8.GetBytes($"[{eventJson}]"));

        Assert.True(parseResult.IsSuccess, parseResult.Failure?.Code);
        return Assert.Single(parseResult.Items);
    }

    public static EventValidationResult ValidateSingle(
        string eventJson,
        CmsEventIngestionLimits? limits = null)
    {
        var item = ParseSingle(eventJson, limits);
        return new EventValidator(limits).Validate(item);
    }

    public static ValidatedCmsEventData ValidateValid(
        string eventJson,
        CmsEventIngestionLimits? limits = null)
    {
        var result = ValidateSingle(eventJson, limits);

        Assert.True(result.IsValid, result.Failure?.Code);
        return Assert.IsType<ValidatedCmsEventData>(result.ValidatedEvent);
    }

    public static string Publish(
        string type = "publish",
        string? eventIdProperty = null,
        string idProperty = "\"id\":\"entity-1\"",
        string versionProperty = "\"version\":7",
        string? timestamp = null,
        string payloadProperty = "\"payload\":{\"name\":\"value\"}",
        string? unknownProperty = null)
    {
        var properties = new List<string>();

        if (eventIdProperty is not null)
        {
            properties.Add(eventIdProperty);
        }

        properties.Add($"\"type\":{System.Text.Json.JsonSerializer.Serialize(type)}");
        properties.Add(idProperty);
        properties.Add(versionProperty);
        properties.Add($"\"timestamp\":\"{timestamp ?? Timestamp}\"");
        properties.Add(payloadProperty);

        if (unknownProperty is not null)
        {
            properties.Add(unknownProperty);
        }

        return $"{{{string.Join(',', properties)}}}";
    }

    public static string Delete(
        string type = "delete",
        string? eventIdProperty = null,
        string idProperty = "\"id\":\"entity-1\"",
        string? timestamp = null,
        string? extraProperty = null)
    {
        var properties = new List<string>();

        if (eventIdProperty is not null)
        {
            properties.Add(eventIdProperty);
        }

        properties.Add($"\"type\":{System.Text.Json.JsonSerializer.Serialize(type)}");
        properties.Add(idProperty);
        properties.Add($"\"timestamp\":\"{timestamp ?? Timestamp}\"");

        if (extraProperty is not null)
        {
            properties.Add(extraProperty);
        }

        return $"{{{string.Join(',', properties)}}}";
    }
}
