namespace NexusPM.Domain.Common;

/// <summary>
/// Base class for all domain entities. Identity-based equality — two entities
/// with the same Id are considered equal regardless of their state.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.NewGuid();

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id == other.Id;
    }

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity? left, Entity? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
