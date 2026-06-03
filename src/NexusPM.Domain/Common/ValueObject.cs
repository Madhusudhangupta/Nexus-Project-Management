namespace NexusPM.Domain.Common;

/// <summary>
/// Base class for value objects. Equality is structural — two value objects
/// are equal if all their components are equal. Value objects are immutable.
/// </summary>
public abstract class ValueObject
{
    /// <summary>Returns all components that participate in equality comparison.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType()) return false;
        return GetEqualityComponents()
            .SequenceEqual(((ValueObject)obj).GetEqualityComponents());
    }

    public override int GetHashCode() =>
        GetEqualityComponents()
            .Aggregate(0, (hash, c) => HashCode.Combine(hash, c?.GetHashCode() ?? 0));

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
