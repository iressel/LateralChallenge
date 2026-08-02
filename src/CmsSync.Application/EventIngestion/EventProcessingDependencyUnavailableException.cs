namespace CmsSync.Application.EventIngestion;

public sealed class EventProcessingDependencyUnavailableException : Exception
{
    public EventProcessingDependencyUnavailableException()
        : base("CMS event persistence is temporarily unavailable.")
    {
    }
}
