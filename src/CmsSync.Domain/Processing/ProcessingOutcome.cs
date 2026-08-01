namespace CmsSync.Domain.Processing;

public enum ProcessingOutcome
{
    Applied,
    Duplicate,
    Equivalent,
    Stale,
    Invalid,
    Conflict,
}
