namespace CmsSync.Application.EventIngestion;

public enum CmsEventType
{
    Publish,
    Unpublish,
    Delete,
}

public static class CmsEventTypeNames
{
    public const string Publish = "publish";
    public const string Unpublish = "unpublish";
    public const string Delete = "delete";

    public static string GetCanonicalName(CmsEventType eventType) => eventType switch
    {
        CmsEventType.Publish => Publish,
        CmsEventType.Unpublish => Unpublish,
        CmsEventType.Delete => Delete,
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unsupported CMS event type."),
    };
}
