using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Common;

namespace NexusPM.Domain.Events.Workspaces;

public sealed record WorkspaceCreatedEvent(
    Guid WorkspaceId,
    Guid OwnerId) : DomainEvent;

public sealed record WorkspaceMemberInvitedEvent(
    Guid WorkspaceId,
    Guid UserId,
    WorkspaceRole Role,
    Guid InvitedById) : DomainEvent;

public sealed record WorkspaceMemberRemovedEvent(
    Guid WorkspaceId,
    Guid UserId,
    Guid RemovedById) : DomainEvent;

public sealed record UserPasswordChangedEvent(
    Guid UserId) : DomainEvent;
