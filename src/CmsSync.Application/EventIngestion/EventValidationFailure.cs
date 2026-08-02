namespace CmsSync.Application.EventIngestion;

public sealed record EventValidationFailure(string Code, string Message);
