namespace CmsSync.Application.EventIngestion;

public sealed class CmsEventIngestionLimits
{
    public const int AbsoluteMaximumRequestSizeBytes = 16 * 1024 * 1024;
    public const int AbsoluteMaximumBatchSize = 50;
    public const int AbsoluteMaximumPayloadSizeBytes = 256 * 1024;
    public const int AbsoluteMaximumJsonDepth = 64;
    public const int MaximumIdentifierLength = 200;
    public const int MaximumTimestampFractionalDigits = 7;

    public CmsEventIngestionLimits(
        int maximumRequestSizeBytes = AbsoluteMaximumRequestSizeBytes,
        int maximumBatchSize = AbsoluteMaximumBatchSize,
        int maximumPayloadSizeBytes = AbsoluteMaximumPayloadSizeBytes,
        int maximumJsonDepth = AbsoluteMaximumJsonDepth)
    {
        MaximumRequestSizeBytes = ValidateLimit(
            maximumRequestSizeBytes,
            AbsoluteMaximumRequestSizeBytes,
            nameof(maximumRequestSizeBytes));
        MaximumBatchSize = ValidateLimit(
            maximumBatchSize,
            AbsoluteMaximumBatchSize,
            nameof(maximumBatchSize));
        MaximumPayloadSizeBytes = ValidateLimit(
            maximumPayloadSizeBytes,
            AbsoluteMaximumPayloadSizeBytes,
            nameof(maximumPayloadSizeBytes));
        MaximumJsonDepth = ValidateLimit(
            maximumJsonDepth,
            AbsoluteMaximumJsonDepth,
            nameof(maximumJsonDepth));
    }

    public int MaximumRequestSizeBytes { get; }

    public int MaximumBatchSize { get; }

    public int MaximumPayloadSizeBytes { get; }

    public int MaximumJsonDepth { get; }

    private static int ValidateLimit(int value, int ceiling, string parameterName)
    {
        if (value is <= 0 || value > ceiling)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                $"The configured limit must be between 1 and {ceiling}.");
        }

        return value;
    }
}
