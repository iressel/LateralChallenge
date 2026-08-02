namespace CmsSync.IntegrationTests.EventIngestion;

internal sealed class InjectedTerminalEventProcessingException : Exception
{
    public InjectedTerminalEventProcessingException()
        : base("Injected terminal event-processing failure.")
    {
    }
}
