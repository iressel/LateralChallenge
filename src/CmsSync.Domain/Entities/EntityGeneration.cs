using System.Globalization;

namespace CmsSync.Domain.Entities;

public readonly record struct EntityGeneration : IComparable<EntityGeneration>
{
    public EntityGeneration(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "An entity generation must be a non-negative SQL Server bigint-compatible value.");
        }

        Value = value;
    }

    public long Value { get; }

    public bool IsActive => Value > 0;

    public int CompareTo(EntityGeneration other)
    {
        return Value.CompareTo(other.Value);
    }

    public bool TryGetNext(out EntityGeneration next)
    {
        if (Value == long.MaxValue)
        {
            next = default;
            return false;
        }

        next = new EntityGeneration(Value + 1);
        return true;
    }

    public override string ToString()
    {
        return Value.ToString(CultureInfo.InvariantCulture);
    }

    public static bool operator <(EntityGeneration left, EntityGeneration right)
    {
        return left.Value < right.Value;
    }

    public static bool operator <=(EntityGeneration left, EntityGeneration right)
    {
        return left.Value <= right.Value;
    }

    public static bool operator >(EntityGeneration left, EntityGeneration right)
    {
        return left.Value > right.Value;
    }

    public static bool operator >=(EntityGeneration left, EntityGeneration right)
    {
        return left.Value >= right.Value;
    }
}
