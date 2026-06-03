using NexusPM.Domain.Common;

namespace NexusPM.Domain.Events.Projects;

public sealed record ProjectCreatedEvent(
    Guid ProjectId,
    Guid WorkspaceId,
    Guid CreatedById) : DomainEvent;

public sealed record SprintCreatedEvent(
    Guid SprintId,
    Guid ProjectId,
    Guid WorkspaceId) : DomainEvent;

public sealed record SprintStartedEvent(
    Guid SprintId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid StartedById) : DomainEvent;

public sealed record SprintClosedEvent(
    Guid SprintId,
    Guid ProjectId,
    Guid WorkspaceId,
    Guid ClosedById,
    int CompletedTaskCount,
    int IncompleteTaskCount) : DomainEvent;
