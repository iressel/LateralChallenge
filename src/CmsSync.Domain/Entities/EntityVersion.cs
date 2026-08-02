using System.Globalization;

namespace CmsSync.Domain.Entities;

public readonly record struct EntityVersion : IComparable<EntityVersion>
{
    public EntityVersion(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "An entity version must be a positive SQL Server bigint-compatible value.");
        }

        Value = value;
    }

    public long Value { get; }

    public bool IsValid => Value > 0;

    public int CompareTo(EntityVersion other)
    {
        return Value.CompareTo(other.Value);
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    public static bool operator <(EntityVersion left, EntityVersion right)
    {
        return left.Value < right.Value;
    }

    public static bool operator <=(EntityVersion left, EntityVersion right)
    {
        return left.Value <= right.Value;
    }

    public static bool operator >(EntityVersion left, EntityVersion right)
    {
        return left.Value > right.Value;
    }

    public static bool operator >=(EntityVersion left, EntityVersion right)
    {
        return left.Value >= right.Value;
    }
}
