namespace CmsSync.Application.EventIngestion;

public sealed record CmsEventRequestFailure(string Code, string Message);

public sealed class ParsedCmsEventItem
{
    internal ParsedCmsEventItem(
        int sequence,
        ReadOnlyMemory<byte> utf8Json,
        bool hasDuplicatePropertyNames)
    {
        Sequence = sequence;
        Utf8Json = utf8Json;
        HasDuplicatePropertyNames = hasDuplicatePropertyNames;
    }

    public int Sequence { get; }

    public bool HasDuplicatePropertyNames { get; }

    internal ReadOnlyMemory<byte> Utf8Json { get; }

    public override string ToString()
    {
        return $"Sequence = {Sequence}, Size = {Utf8Json.Length}, HasDuplicates = {HasDuplicatePropertyNames}";
    }
}

public sealed class CmsEventArrayParseResult
{
    private static readonly IReadOnlyList<ParsedCmsEventItem> NoItems = [];

    private CmsEventArrayParseResult(
        IReadOnlyList<ParsedCmsEventItem> items,
        CmsEventRequestFailure? failure)
    {
        Items = items;
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public IReadOnlyList<ParsedCmsEventItem> Items { get; }

    public CmsEventRequestFailure? Failure { get; }

    internal static CmsEventArrayParseResult Success(List<ParsedCmsEventItem> items)
    {
        return new CmsEventArrayParseResult(items.ToArray(), null);
    }

    internal static CmsEventArrayParseResult Failed(string code, string message)
    {
        return new CmsEventArrayParseResult(NoItems, new CmsEventRequestFailure(code, message));
    }

    public override string ToString()
    {
        return IsSuccess
            ? $"Success, ItemCount = {Items.Count}"
            : $"Failure, Code = {Failure!.Code}";
    }
}
