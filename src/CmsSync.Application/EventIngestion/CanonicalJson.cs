using System.Text.Json;

namespace CmsSync.Application.EventIngestion;

public static class CanonicalJson
{
    private const byte NullTag = 0;
    private const byte FalseTag = 1;
    private const byte TrueTag = 2;
    private const byte NumberTag = 3;
    private const byte StringTag = 4;
    private const byte ArrayTag = 5;
    private const byte ObjectTag = 6;

    public static byte[] Canonicalize(ReadOnlyMemory<byte> utf8Json, int maximumDepth = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);

        using var document = JsonDocument.Parse(
            utf8Json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = maximumDepth,
            });

        return Canonicalize(document.RootElement);
    }

    internal static byte[] Canonicalize(JsonElement element)
    {
        var writer = new LengthPrefixedEncodingWriter();
        WriteElement(writer, element);
        return writer.ToArray();
    }

    private static void WriteElement(LengthPrefixedEncodingWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
                writer.WriteByte(NullTag);
                break;
            case JsonValueKind.False:
                writer.WriteByte(FalseTag);
                break;
            case JsonValueKind.True:
                writer.WriteByte(TrueTag);
                break;
            case JsonValueKind.Number:
                writer.WriteByte(NumberTag);
                writer.WriteLengthPrefixed(element.GetRawText());
                break;
            case JsonValueKind.String:
                writer.WriteByte(StringTag);
                writer.WriteLengthPrefixed(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                WriteArray(writer, element);
                break;
            case JsonValueKind.Object:
                WriteObject(writer, element);
                break;
            default:
                throw new InvalidOperationException("The JSON value kind cannot be canonicalized.");
        }
    }

    private static void WriteArray(LengthPrefixedEncodingWriter writer, JsonElement element)
    {
        writer.WriteByte(ArrayTag);
        writer.WriteInt32(element.GetArrayLength());

        foreach (var item in element.EnumerateArray())
        {
            WriteElement(writer, item);
        }
    }

    private static void WriteObject(LengthPrefixedEncodingWriter writer, JsonElement element)
    {
        var properties = new List<JsonProperty>();
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!propertyNames.Add(property.Name))
            {
                throw new JsonException("Duplicate JSON property names cannot be canonicalized.");
            }

            properties.Add(property);
        }

        properties.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        writer.WriteByte(ObjectTag);
        writer.WriteInt32(properties.Count);

        foreach (var property in properties)
        {
            writer.WriteLengthPrefixed(property.Name);
            WriteElement(writer, property.Value);
        }
    }
}
