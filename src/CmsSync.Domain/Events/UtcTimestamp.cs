using System.Globalization;

namespace CmsSync.Domain.Events;

public readonly record struct UtcTimestamp : IComparable<UtcTimestamp>
{
    public UtcTimestamp(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("The timestamp must already be normalized to UTC.", nameof(value));
        }

        Value = value;
    }

    public DateTimeOffset Value { get; }

    public int CompareTo(UtcTimestamp other)
    {
        return Value.CompareTo(other.Value);
    }

    public override string ToString()
    {
        return Value.ToString("O", CultureInfo.InvariantCulture);
    }

    public static UtcTimestamp Max(UtcTimestamp left, UtcTimestamp right)
    {
        return left >= right ? left : right;
    }

    public static bool operator <(UtcTimestamp left, UtcTimestamp right)
    {
        return left.Value < right.Value;
    }

    public static bool operator <=(UtcTimestamp left, UtcTimestamp right)
    {
        return left.Value <= right.Value;
    }

    public static bool operator >(UtcTimestamp left, UtcTimestamp right)
    {
        return left.Value > right.Value;
    }

    public static bool operator >=(UtcTimestamp left, UtcTimestamp right)
    {
        return left.Value >= right.Value;
    }
}
