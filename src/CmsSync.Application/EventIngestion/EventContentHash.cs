namespace CmsSync.Application.EventIngestion;

public sealed class EventContentHash : IEquatable<EventContentHash>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    public EventContentHash(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"An event content hash must contain exactly {Length} bytes.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public bool Equals(EventContentHash? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is EventContentHash other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();

        foreach (var value in _bytes)
        {
            hashCode.Add(value);
        }

        return hashCode.ToHashCode();
    }

    public byte[] ToArray() => (byte[])_bytes.Clone();

    public override string ToString() => Convert.ToHexString(_bytes);

    public static bool operator ==(EventContentHash? left, EventContentHash? right) => Equals(left, right);

    public static bool operator !=(EventContentHash? left, EventContentHash? right) => !Equals(left, right);
}
