namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class InjectedTransientEventProcessingException : Exception
{
    public InjectedTransientEventProcessingException()
        : base("Injected transient event-processing failure.")
    {
    }
}
