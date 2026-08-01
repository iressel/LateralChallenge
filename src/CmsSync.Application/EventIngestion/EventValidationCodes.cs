namespace CmsSync.Application.EventIngestion;

public static class EventValidationCodes
{
    public const string EventMustBeObject = "EVENT_MUST_BE_OBJECT";
    public const string DuplicatePropertyName = "DUPLICATE_PROPERTY_NAME";
    public const string EventTypeRequired = "EVENT_TYPE_REQUIRED";
    public const string EventTypeInvalid = "EVENT_TYPE_INVALID";
    public const string EntityIdRequired = "ENTITY_ID_REQUIRED";
    public const string EntityIdInvalid = "ENTITY_ID_INVALID";
    public const string EventIdInvalid = "EVENT_ID_INVALID";
    public const string TimestampRequired = "TIMESTAMP_REQUIRED";
    public const string TimestampInvalid = "TIMESTAMP_INVALID";
    public const string VersionRequired = "VERSION_REQUIRED";
    public const string VersionInvalid = "VERSION_INVALID";
    public const string VersionNotAllowed = "VERSION_NOT_ALLOWED";
    public const string PayloadRequired = "PAYLOAD_REQUIRED";
    public const string PayloadMustBeObject = "PAYLOAD_MUST_BE_OBJECT";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string PayloadNotAllowed = "PAYLOAD_NOT_ALLOWED";
}
