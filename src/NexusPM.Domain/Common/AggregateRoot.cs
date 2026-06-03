namespace NexusPM.Domain.Common;

/// <summary>
/// Base class for all aggregate roots. Holds domain events and enforces
/// that only the aggregate root controls mutations to the aggregate.
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Immutable snapshot of uncommitted domain events.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>Raises a domain event, adding it to the uncommitted list.</summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent) =>
        _domainEvents.Add(domainEvent);

    /// <summary>
    /// Clears uncommitted events after they have been dispatched.
    /// Called by the Unit of Work after SaveChangesAsync.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
