namespace CmsSync.Domain.Processing;

public sealed record ProcessingCode
{
    internal ProcessingCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString()
    {
        return Value;
    }
}
