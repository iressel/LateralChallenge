namespace CmsSync.Application.EventIngestion;

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
