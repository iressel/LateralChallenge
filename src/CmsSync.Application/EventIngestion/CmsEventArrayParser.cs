using System.Text.Json;

namespace CmsSync.Application.EventIngestion;

public sealed class CmsEventArrayParser
{
    private readonly CmsEventIngestionLimits _limits;

    public CmsEventArrayParser(CmsEventIngestionLimits? limits = null)
    {
        _limits = limits ?? new CmsEventIngestionLimits();
    }

    public int MaximumRequestSizeBytes => _limits.MaximumRequestSizeBytes;

    public CmsEventArrayParseResult Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.Length > _limits.MaximumRequestSizeBytes)
        {
            return CmsEventArrayParseResult.Failed(
                CmsEventParsingCodes.RequestTooLarge,
                "The request exceeds the configured byte limit.");
        }

        try
        {
            return ParseDocument(utf8Json);
        }
        catch (JsonException)
        {
            return CmsEventArrayParseResult.Failed(
                CmsEventParsingCodes.MalformedJson,
                "The request body is not a valid JSON document within the supported nesting depth.");
        }
    }

    private CmsEventArrayParseResult ParseDocument(ReadOnlyMemory<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(
            utf8Json.Span,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = _limits.MaximumJsonDepth,
            });

        if (!reader.Read())
        {
            throw new JsonException();
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            ConsumeValue(ref reader);
            EnsureEndOfDocument(ref reader);

            return CmsEventArrayParseResult.Failed(
                CmsEventParsingCodes.InvalidEnvelope,
                "The request body must be a raw top-level JSON array.");
        }

        var items = new List<ParsedCmsEventItem>();
        var itemCount = 0;
        var reachedArrayEnd = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                reachedArrayEnd = true;
                break;
            }

            var startIndex = checked((int)reader.TokenStartIndex);
            var hasDuplicatePropertyNames = ConsumeValue(ref reader);
            var endIndex = checked((int)reader.BytesConsumed);

            if (itemCount < _limits.MaximumBatchSize)
            {
                items.Add(
                    new ParsedCmsEventItem(
                        itemCount,
                        utf8Json.Slice(startIndex, endIndex - startIndex),
                        hasDuplicatePropertyNames));
            }

            itemCount++;
        }

        if (!reachedArrayEnd)
        {
            throw new JsonException();
        }

        EnsureEndOfDocument(ref reader);

        if (itemCount is 0 || itemCount > _limits.MaximumBatchSize)
        {
            return CmsEventArrayParseResult.Failed(
                CmsEventParsingCodes.BatchSizeOutOfRange,
                $"The event array must contain between 1 and {_limits.MaximumBatchSize} items.");
        }

        return CmsEventArrayParseResult.Success(items);
    }

    private static bool ConsumeValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not JsonTokenType.StartObject and not JsonTokenType.StartArray)
        {
            return false;
        }

        var remainingContainers = 1;
        var objectPropertyNames = new Stack<HashSet<string>>();
        var hasDuplicatePropertyNames = false;

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            objectPropertyNames.Push(new HashSet<string>(StringComparer.Ordinal));
        }

        while (remainingContainers > 0)
        {
            if (!reader.Read())
            {
                throw new JsonException();
            }

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    remainingContainers++;
                    objectPropertyNames.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    remainingContainers--;
                    objectPropertyNames.Pop();
                    break;
                case JsonTokenType.StartArray:
                    remainingContainers++;
                    break;
                case JsonTokenType.EndArray:
                    remainingContainers--;
                    break;
                case JsonTokenType.PropertyName:
                    var propertyName = reader.GetString()
                        ?? throw new JsonException();
                    hasDuplicatePropertyNames |= !objectPropertyNames.Peek().Add(propertyName);
                    break;
            }
        }

        return hasDuplicatePropertyNames;
    }

    private static void EnsureEndOfDocument(ref Utf8JsonReader reader)
    {
        if (reader.Read())
        {
            throw new JsonException();
        }
    }
}
