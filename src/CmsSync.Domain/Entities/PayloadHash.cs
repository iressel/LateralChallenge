namespace CmsSync.Domain.Entities;

public sealed class PayloadHash : IEquatable<PayloadHash>
{
    public const int Length = 32;

    private readonly byte[] _bytes;

    public PayloadHash(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException($"A payload hash must contain exactly {Length} bytes.", nameof(bytes));
        }

        _bytes = bytes.ToArray();
    }

    public bool Equals(PayloadHash? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => obj is PayloadHash other && Equals(other);

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

    public static bool operator ==(PayloadHash? left, PayloadHash? right) => Equals(left, right);

    public static bool operator !=(PayloadHash? left, PayloadHash? right) => !Equals(left, right);
}
