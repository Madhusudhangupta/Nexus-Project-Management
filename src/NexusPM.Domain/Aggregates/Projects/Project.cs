using NexusPM.Domain.Common;
using NexusPM.Domain.Events.Projects;
using NexusPM.Domain.Exceptions;

namespace NexusPM.Domain.Aggregates.Projects;

/// <summary>Sprint lifecycle states.</summary>
public enum SprintStatus { Planned, Active, Closed }

/// <summary>
/// Project aggregate root. Contains workflow states, sprints, and settings.
/// A project belongs to exactly one workspace.
/// Invariant: must have at least one terminal workflow state.
/// </summary>
public sealed class Project : AggregateRoot, IAuditableEntity, ISoftDeletable, ITenantEntity
{
    private readonly List<WorkflowState> _workflowStates = [];
    private readonly List<Sprint> _sprints = [];

    public Guid WorkspaceId { get; private init; }
    public string Name { get; private set; } = null!;

    /// <summary>Short key used in task identifiers, e.g. "NEXUS" → "NEXUS-42".</summary>
    public string Key { get; private init; } = null!;
    public string? Description { get; private set; }
    public bool IsArchived { get; private set; }

    public IReadOnlyCollection<WorkflowState> WorkflowStates => _workflowStates.AsReadOnly();
    public IReadOnlyCollection<Sprint> Sprints => _sprints.AsReadOnly();

    // ── Audit ─────────────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; private init; }
    public Guid CreatedById { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Project() { }

    public static Project Create(
        Guid workspaceId, string name, string key, string? description, Guid createdById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        key = key.Trim().ToUpperInvariant();
        ValidateKey(key);

        var now = DateTimeOffset.UtcNow;
        var project = new Project
        {
            Id          = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Name        = name.Trim(),
            Key         = key,
            Description = description?.Trim(),
            CreatedAt   = now,
            CreatedById = createdById,
            UpdatedAt   = now,
        };

        // Seed default workflow states
        project._workflowStates.AddRange(WorkflowState.CreateDefaults(project.Id));

        project.RaiseDomainEvent(new ProjectCreatedEvent(project.Id, workspaceId, createdById));
        return project;
    }

    public void Update(string name, string? description, Guid updatedById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
    }

    public void Archive(Guid updatedById)
    {
        if (IsArchived)
            throw new BusinessRuleViolationException("ProjectArchive", "Project is already archived.");
        IsArchived = true;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
        SoftDelete(updatedById);
    }

    /// <summary>Adds a custom workflow state to the project.</summary>
    public WorkflowState AddWorkflowState(string name, string color, bool isTerminal, int position, Guid updatedById)
    {
        if (_workflowStates.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new DuplicateException("WorkflowState", "name", name);

        var state = WorkflowState.Create(Id, name, color, isTerminal, position);
        _workflowStates.Add(state);
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
        return state;
    }

    /// <summary>Creates a sprint for this project.</summary>
    public Sprint CreateSprint(string name, string? goal, DateOnly? startDate, DateOnly? endDate, Guid createdById)
    {
        if (_sprints.Any(s => s.Status == SprintStatus.Active))
            throw new BusinessRuleViolationException(
                "SingleActiveSprint", "A project can only have one active sprint at a time.");

        if (startDate.HasValue && endDate.HasValue && startDate > endDate)
            throw new DomainException("Sprint start date must be before end date.");

        var sprint = Sprint.Create(Id, WorkspaceId, name, goal, startDate, endDate, createdById);
        _sprints.Add(sprint);
        RaiseDomainEvent(new SprintCreatedEvent(sprint.Id, Id, WorkspaceId));
        return sprint;
    }

    public WorkflowState GetWorkflowState(Guid stateId) =>
        _workflowStates.FirstOrDefault(s => s.Id == stateId)
            ?? throw new NotFoundException("WorkflowState", stateId);

    public WorkflowState GetDefaultState() =>
        _workflowStates.OrderBy(s => s.Position).First();

    public void SoftDelete(Guid deletedById)
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DeletedAt.Value;
        UpdatedById = deletedById;
    }

    private static void ValidateKey(string key)
    {
        if (key.Length < 2 || key.Length > 10)
            throw new DomainException("Project key must be 2-10 characters.");
        if (!key.All(c => char.IsLetterOrDigit(c)))
            throw new DomainException("Project key must contain only letters and digits.");
    }
}

/// <summary>A named status that tasks can hold within a project workflow.</summary>
public sealed class WorkflowState : Entity
{
    public Guid ProjectId { get; private init; }
    public string Name { get; private set; } = null!;
    public string Color { get; private set; } = null!;

    /// <summary>Terminal states represent completed or cancelled tasks.</summary>
    public bool IsTerminal { get; private set; }

    /// <summary>Display order on the Kanban board.</summary>
    public int Position { get; private set; }

    private WorkflowState() { }

    internal static WorkflowState Create(
        Guid projectId, string name, string color, bool isTerminal, int position)
    {
        return new WorkflowState
        {
            Id         = Guid.NewGuid(),
            ProjectId  = projectId,
            Name       = name,
            Color      = color,
            IsTerminal = isTerminal,
            Position   = position,
        };
    }

    internal static IEnumerable<WorkflowState> CreateDefaults(Guid projectId) =>
    [
        Create(projectId, "Backlog",     "#6B7280", isTerminal: false, position: 0),
        Create(projectId, "In Progress", "#3B82F6", isTerminal: false, position: 1),
        Create(projectId, "In Review",   "#F59E0B", isTerminal: false, position: 2),
        Create(projectId, "Done",        "#10B981", isTerminal: true,  position: 3),
    ];

    public void Update(string name, string color, bool isTerminal, int position)
    {
        Name = name;
        Color = color;
        IsTerminal = isTerminal;
        Position = position;
    }
}

/// <summary>A time-boxed iteration (sprint) within a project.</summary>
public sealed class Sprint : Entity, IAuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; private init; }
    public Guid WorkspaceId { get; private init; }
    public string Name { get; private set; } = null!;
    public string? Goal { get; private set; }
    public SprintStatus Status { get; private set; }
    public DateOnly? StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private init; }
    public Guid CreatedById { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }

    private Sprint() { }

    internal static Sprint Create(
        Guid projectId, Guid workspaceId, string name, string? goal,
        DateOnly? startDate, DateOnly? endDate, Guid createdById)
    {
        var now = DateTimeOffset.UtcNow;
        return new Sprint
        {
            Id          = Guid.NewGuid(),
            ProjectId   = projectId,
            WorkspaceId = workspaceId,
            Name        = name.Trim(),
            Goal        = goal?.Trim(),
            Status      = SprintStatus.Planned,
            StartDate   = startDate,
            EndDate     = endDate,
            CreatedAt   = now,
            CreatedById = createdById,
            UpdatedAt   = now,
        };
    }

    public void Start(Guid updatedById)
    {
        if (Status != SprintStatus.Planned)
            throw new InvalidStateTransitionException(Status.ToString(), "Active");

        Status = SprintStatus.Active;
        StartDate ??= DateOnly.FromDateTime(DateTime.UtcNow);
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
    }

    public void Close(Guid updatedById)
    {
        if (Status != SprintStatus.Active)
            throw new InvalidStateTransitionException(Status.ToString(), "Closed");

        Status = SprintStatus.Closed;
        ClosedAt = DateTimeOffset.UtcNow;
        UpdatedAt = ClosedAt.Value;
        UpdatedById = updatedById;
    }
}
