using NexusPM.Domain.Common;

namespace NexusPM.Domain.Events.Tasks;

public sealed record TaskCreatedEvent(
    Guid TaskId,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid ReporterId) : DomainEvent;

public sealed record TaskAssignedEvent(
    Guid TaskId,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid AssigneeId,
    Guid AssignedById) : DomainEvent;

public sealed record TaskStatusChangedEvent(
    Guid TaskId,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid OldStatusId,
    Guid NewStatusId,
    Guid ChangedById) : DomainEvent;

public sealed record TaskCommentAddedEvent(
    Guid TaskId,
    Guid WorkspaceId,
    Guid CommentId,
    Guid AuthorId) : DomainEvent;

public sealed record TaskDeletedEvent(
    Guid TaskId,
    Guid WorkspaceId,
    Guid ProjectId,
    Guid DeletedById) : DomainEvent;
