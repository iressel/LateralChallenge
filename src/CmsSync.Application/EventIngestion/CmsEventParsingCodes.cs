namespace CmsSync.Application.EventIngestion;

public static class CmsEventParsingCodes
{
    public const string RequestTooLarge = "REQUEST_TOO_LARGE";
    public const string MalformedJson = "MALFORMED_JSON";
    public const string InvalidEnvelope = "INVALID_ENVELOPE";
    public const string BatchSizeOutOfRange = "BATCH_SIZE_OUT_OF_RANGE";
}
