using NexusPM.Domain.Common;
using NexusPM.Domain.Events.Workspaces;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.ValueObjects;

namespace NexusPM.Domain.Aggregates.Workspaces;

/// <summary>Subscription tier controlling feature availability.</summary>
public enum SubscriptionTier { Free, Pro, Enterprise }

/// <summary>Role within a workspace.</summary>
public enum WorkspaceRole { Admin, Member, Viewer }

/// <summary>
/// Workspace aggregate root — the top-level tenant container.
/// All projects, tasks, and data belong to a workspace.
/// Invariant: must always have at least one Admin member.
/// </summary>
public sealed class Workspace : AggregateRoot, IAuditableEntity, ISoftDeletable
{
    private readonly List<WorkspaceMembership> _memberships = [];

    public WorkspaceSlug Slug { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? LogoUrl { get; private set; }
    public SubscriptionTier Tier { get; private set; }
    public Guid OwnerId { get; private init; }

    public IReadOnlyCollection<WorkspaceMembership> Memberships => _memberships.AsReadOnly();

    // ── Audit ─────────────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; private init; }
    public Guid CreatedById { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Workspace() { }

    /// <summary>
    /// Creates a workspace and automatically adds the creator as Admin.
    /// </summary>
    public static Workspace Create(string name, Guid ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();

        if (name.Length > 100)
            throw new DomainException("Workspace name must not exceed 100 characters.");

        var now = DateTimeOffset.UtcNow;
        var workspace = new Workspace
        {
            Id          = Guid.NewGuid(),
            Name        = name,
            Slug        = WorkspaceSlug.FromName(name),
            Tier        = SubscriptionTier.Free,
            OwnerId     = ownerId,
            CreatedAt   = now,
            CreatedById = ownerId,
            UpdatedAt   = now,
        };

        // Owner is always the first admin
        workspace._memberships.Add(WorkspaceMembership.Create(
            workspace.Id, ownerId, WorkspaceRole.Admin, invitedById: null));

        workspace.RaiseDomainEvent(new WorkspaceCreatedEvent(workspace.Id, ownerId));
        return workspace;
    }

    /// <summary>Invites a user to the workspace.</summary>
    public WorkspaceMembership InviteMember(Guid userId, WorkspaceRole role, Guid invitedById)
    {
        if (_memberships.Any(m => m.UserId == userId))
            throw new DuplicateException("WorkspaceMembership", "userId", userId);

        var membership = WorkspaceMembership.Create(Id, userId, role, invitedById);
        _memberships.Add(membership);

        RaiseDomainEvent(new WorkspaceMemberInvitedEvent(Id, userId, role, invitedById));
        return membership;
    }

    /// <summary>Changes the role of an existing member.</summary>
    public void ChangeMemberRole(Guid userId, WorkspaceRole newRole, Guid changedById)
    {
        var membership = GetMembership(userId);

        // Guard: must have at least one Admin
        if (membership.Role == WorkspaceRole.Admin && newRole != WorkspaceRole.Admin)
        {
            var adminCount = _memberships.Count(m => m.Role == WorkspaceRole.Admin);
            if (adminCount <= 1)
                throw new BusinessRuleViolationException(
                    "LastAdminProtection",
                    "Cannot remove the last admin. Assign another admin first.");
        }

        membership.ChangeRole(newRole);
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = changedById;
    }

    /// <summary>Removes a member from the workspace.</summary>
    public void RemoveMember(Guid userId, Guid removedById)
    {
        var membership = GetMembership(userId);

        if (membership.Role == WorkspaceRole.Admin)
        {
            var adminCount = _memberships.Count(m => m.Role == WorkspaceRole.Admin);
            if (adminCount <= 1)
                throw new BusinessRuleViolationException(
                    "LastAdminProtection",
                    "Cannot remove the last admin from a workspace.");
        }

        _memberships.Remove(membership);
        RaiseDomainEvent(new WorkspaceMemberRemovedEvent(Id, userId, removedById));
    }

    public void Update(string name, string? logoUrl, Guid updatedById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();
        if (name.Length > 100)
            throw new DomainException("Workspace name must not exceed 100 characters.");

        Name = name;
        LogoUrl = logoUrl;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
    }

    public void UpgradeTier(SubscriptionTier tier, Guid updatedById)
    {
        Tier = tier;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
    }

    public void SoftDelete(Guid deletedById)
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DeletedAt.Value;
        UpdatedById = deletedById;
    }

    public bool HasMember(Guid userId) =>
        _memberships.Any(m => m.UserId == userId);

    public WorkspaceRole? GetMemberRole(Guid userId) =>
        _memberships.FirstOrDefault(m => m.UserId == userId)?.Role;

    private WorkspaceMembership GetMembership(Guid userId) =>
        _memberships.FirstOrDefault(m => m.UserId == userId)
            ?? throw new NotFoundException("WorkspaceMembership", userId);
}

/// <summary>
/// Represents a user's membership in a workspace with a specific role.
/// Owned by the Workspace aggregate — never modified directly.
/// </summary>
public sealed class WorkspaceMembership : Entity
{
    public Guid WorkspaceId { get; private init; }
    public Guid UserId { get; private init; }
    public WorkspaceRole Role { get; private set; }
    public Guid? InvitedById { get; private init; }
    public DateTimeOffset? JoinedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }

    private WorkspaceMembership() { }

    internal static WorkspaceMembership Create(
        Guid workspaceId, Guid userId, WorkspaceRole role, Guid? invitedById)
    {
        return new WorkspaceMembership
        {
            Id          = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UserId      = userId,
            Role        = role,
            InvitedById = invitedById,
            JoinedAt    = invitedById is null ? DateTimeOffset.UtcNow : null, // direct join vs invitation
            CreatedAt   = DateTimeOffset.UtcNow,
        };
    }

    internal void ChangeRole(WorkspaceRole newRole) => Role = newRole;

    public void MarkJoined() => JoinedAt = DateTimeOffset.UtcNow;
}
