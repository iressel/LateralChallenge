namespace CmsSync.Application.EventIngestion;

public sealed class ParsedCmsEventItem
{
    internal ParsedCmsEventItem(
        int sequence,
        ReadOnlyMemory<byte> utf8Json,
        bool hasDuplicatePropertyNames)
    {
        Sequence = sequence;
        Utf8Json = utf8Json;
        HasDuplicatePropertyNames = hasDuplicatePropertyNames;
    }

    public int Sequence { get; }

    public bool HasDuplicatePropertyNames { get; }

    internal ReadOnlyMemory<byte> Utf8Json { get; }

    public override string ToString()
    {
        return $"Sequence = {Sequence}, Size = {Utf8Json.Length}, HasDuplicates = {HasDuplicatePropertyNames}";
    }
}
