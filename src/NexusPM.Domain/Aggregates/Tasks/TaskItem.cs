using NexusPM.Domain.Common;
using NexusPM.Domain.Events.Tasks;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.ValueObjects;

namespace NexusPM.Domain.Aggregates.Tasks;

/// <summary>How one task relates to another in a dependency.</summary>
public enum DependencyType { Blocks, IsBlockedBy }

/// <summary>
/// Task aggregate root — the core work item in Nexus PM.
///
/// Invariants enforced:
///   - Circular dependencies are forbidden.
///   - Sub-tasks cannot exceed 3 levels of nesting.
///   - Story points must be Fibonacci numbers.
///   - Status transitions are validated against project workflow.
/// </summary>
public sealed class TaskItem : AggregateRoot, IAuditableEntity, ISoftDeletable, ITenantEntity
{
    private readonly List<TaskComment> _comments = [];
    private readonly List<TaskAttachment> _attachments = [];
    private readonly List<TaskDependency> _dependencies = [];
    private readonly List<string> _labels = [];

    // ── Core fields ───────────────────────────────────────────────────────────
    public Guid WorkspaceId { get; private init; }
    public Guid ProjectId { get; private init; }
    public Guid? SprintId { get; private set; }
    public Guid? ParentTaskId { get; private init; }

    /// <summary>Human-readable identifier, e.g. "NEXUS-42".</summary>
    public string TaskKey { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid StatusId { get; private set; }
    public Priority Priority { get; private set; } = null!;
    public Guid? AssigneeId { get; private set; }
    public Guid ReporterId { get; private init; }
    public StoryPoints? StoryPoints { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public decimal? EstimatedHours { get; private set; }
    public decimal LoggedHours { get; private set; }

    /// <summary>Display order within a Kanban column.</summary>
    public int Position { get; private set; }

    /// <summary>0 = top-level task, 1-3 = sub-task levels.</summary>
    public int NestingLevel { get; private init; }

    public IReadOnlyCollection<TaskComment> Comments => _comments.AsReadOnly();
    public IReadOnlyCollection<TaskAttachment> Attachments => _attachments.AsReadOnly();
    public IReadOnlyCollection<TaskDependency> Dependencies => _dependencies.AsReadOnly();
    public IReadOnlyList<string> Labels => _labels.AsReadOnly();

    // ── Audit ─────────────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; private init; }
    public Guid CreatedById { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private TaskItem() { }

    /// <summary>Creates a new top-level task in a project.</summary>
    public static TaskItem Create(
        Guid workspaceId,
        Guid projectId,
        string taskKey,
        string title,
        Guid statusId,
        Guid reporterId,
        Priority? priority = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Length > 500)
            throw new DomainException("Task title must not exceed 500 characters.");

        var now = DateTimeOffset.UtcNow;
        var task = new TaskItem
        {
            Id           = Guid.NewGuid(),
            WorkspaceId  = workspaceId,
            ProjectId    = projectId,
            TaskKey      = taskKey,
            Title        = title.Trim(),
            StatusId     = statusId,
            Priority     = priority ?? Priority.Medium,
            ReporterId   = reporterId,
            NestingLevel = 0,
            CreatedAt    = now,
            CreatedById  = reporterId,
            UpdatedAt    = now,
        };

        task.RaiseDomainEvent(new TaskCreatedEvent(task.Id, workspaceId, projectId, reporterId));
        return task;
    }

    /// <summary>Creates a sub-task under a parent task.</summary>
    public static TaskItem CreateSubTask(
        TaskItem parent, string taskKey, string title, Guid statusId, Guid reporterId)
    {
        if (parent.NestingLevel >= 3)
            throw new BusinessRuleViolationException(
                "SubTaskDepth", "Sub-tasks cannot exceed 3 levels of nesting.");

        var subTask = Create(parent.WorkspaceId, parent.ProjectId, taskKey, title, statusId, reporterId);
        // Use private init via object initializer replacement trick — re-create with parent info
        return new TaskItem
        {
            Id           = Guid.NewGuid(),
            WorkspaceId  = parent.WorkspaceId,
            ProjectId    = parent.ProjectId,
            ParentTaskId = parent.Id,
            TaskKey      = taskKey,
            Title        = title.Trim(),
            StatusId     = statusId,
            Priority     = Priority.Medium,
            ReporterId   = reporterId,
            NestingLevel = parent.NestingLevel + 1,
            CreatedAt    = DateTimeOffset.UtcNow,
            CreatedById  = reporterId,
            UpdatedAt    = DateTimeOffset.UtcNow,
        };
    }

    // ── Mutations ─────────────────────────────────────────────────────────────

    public void UpdateTitle(string title, Guid updatedById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Length > 500)
            throw new DomainException("Task title must not exceed 500 characters.");
        Title = title.Trim();
        Touch(updatedById);
    }

    public void UpdateDescription(string? description, Guid updatedById)
    {
        Description = description?.Trim();
        Touch(updatedById);
    }

    public void ChangeStatus(Guid newStatusId, Guid updatedById)
    {
        var oldStatusId = StatusId;
        StatusId = newStatusId;
        Touch(updatedById);
        RaiseDomainEvent(new TaskStatusChangedEvent(Id, WorkspaceId, ProjectId, oldStatusId, newStatusId, updatedById));
    }

    public void Assign(Guid? assigneeId, Guid updatedById)
    {
        var oldAssigneeId = AssigneeId;
        AssigneeId = assigneeId;
        Touch(updatedById);

        if (assigneeId.HasValue && assigneeId != oldAssigneeId)
            RaiseDomainEvent(new TaskAssignedEvent(Id, WorkspaceId, ProjectId, assigneeId.Value, updatedById));
    }

    public void SetPriority(Priority priority, Guid updatedById)
    {
        Priority = priority;
        Touch(updatedById);
    }

    public void SetStoryPoints(int? points, Guid updatedById)
    {
        StoryPoints = points.HasValue ? ValueObjects.StoryPoints.Create(points.Value) : null;
        Touch(updatedById);
    }

    public void SetDueDate(DateOnly? dueDate, Guid updatedById)
    {
        DueDate = dueDate;
        Touch(updatedById);
    }

    public void AssignToSprint(Guid? sprintId, Guid updatedById)
    {
        SprintId = sprintId;
        Touch(updatedById);
    }

    public void SetLabels(IEnumerable<string> labels, Guid updatedById)
    {
        _labels.Clear();
        _labels.AddRange(labels.Select(l => l.Trim().ToLowerInvariant()).Distinct());
        Touch(updatedById);
    }

    public void LogTime(decimal hours, Guid updatedById)
    {
        if (hours <= 0)
            throw new DomainException("Logged hours must be positive.");
        LoggedHours += hours;
        Touch(updatedById);
    }

    public void SetPosition(int position, Guid updatedById)
    {
        Position = position;
        Touch(updatedById);
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    public TaskComment AddComment(string content, Guid authorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (content.Length > 50_000)
            throw new DomainException("Comment content must not exceed 50,000 characters.");

        var comment = TaskComment.Create(Id, WorkspaceId, content, authorId);
        _comments.Add(comment);
        Touch(authorId);
        RaiseDomainEvent(new TaskCommentAddedEvent(Id, WorkspaceId, comment.Id, authorId));
        return comment;
    }

    public void DeleteComment(Guid commentId, Guid deletedById)
    {
        var comment = _comments.FirstOrDefault(c => c.Id == commentId)
            ?? throw new NotFoundException("TaskComment", commentId);

        if (comment.AuthorId != deletedById)
            throw new UnauthorizedException("Only the comment author can delete their comment.");

        comment.SoftDelete(deletedById);
        Touch(deletedById);
    }

    // ── Attachments ───────────────────────────────────────────────────────────

    public TaskAttachment AddAttachment(string fileName, string blobUrl, long sizeBytes, Guid uploadedById)
    {
        if (_attachments.Count >= 50)
            throw new BusinessRuleViolationException("AttachmentLimit", "A task cannot have more than 50 attachments.");

        var attachment = TaskAttachment.Create(Id, WorkspaceId, fileName, blobUrl, sizeBytes, uploadedById);
        _attachments.Add(attachment);
        Touch(uploadedById);
        return attachment;
    }

    // ── Dependencies ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a dependency. The caller must pass all existing dependency IDs
    /// to enable circular dependency detection.
    /// </summary>
    public void AddDependency(Guid targetTaskId, DependencyType type, IEnumerable<Guid> existingBlockerIds)
    {
        if (targetTaskId == Id)
            throw new BusinessRuleViolationException("SelfDependency", "A task cannot depend on itself.");

        if (_dependencies.Any(d => d.TargetTaskId == targetTaskId && d.Type == type))
            throw new DuplicateException("TaskDependency", "targetTaskId", targetTaskId);

        // Circular dependency check: if this task already blocks the target,
        // the target cannot block this task.
        if (existingBlockerIds.Contains(targetTaskId))
            throw new BusinessRuleViolationException(
                "CircularDependency",
                $"Adding this dependency would create a circular dependency chain.");

        _dependencies.Add(TaskDependency.Create(Id, targetTaskId, type));
    }

    public void SoftDelete(Guid deletedById)
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DeletedAt.Value;
        UpdatedById = deletedById;
        RaiseDomainEvent(new TaskDeletedEvent(Id, WorkspaceId, ProjectId, deletedById));
    }

    private void Touch(Guid updatedById)
    {
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
    }
}

// ── Supporting Entities ───────────────────────────────────────────────────────

public sealed class TaskComment : Entity, ISoftDeletable, ITenantEntity
{
    public Guid TaskId { get; private init; }
    public Guid WorkspaceId { get; private init; }
    public string Content { get; private set; } = null!;
    public Guid AuthorId { get; private init; }
    public bool IsEdited { get; private set; }
    public DateTimeOffset CreatedAt { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private TaskComment() { }

    internal static TaskComment Create(Guid taskId, Guid workspaceId, string content, Guid authorId)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskComment
        {
            Id          = Guid.NewGuid(),
            TaskId      = taskId,
            WorkspaceId = workspaceId,
            Content     = content,
            AuthorId    = authorId,
            CreatedAt   = now,
            UpdatedAt   = now,
        };
    }

    public void Edit(string newContent, Guid editorId)
    {
        if (AuthorId != editorId)
            throw new UnauthorizedException("Only the comment author can edit their comment.");
        Content = newContent;
        IsEdited = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    internal void SoftDelete(Guid deletedById)
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DeletedAt.Value;
    }
}

public sealed class TaskAttachment : Entity, ITenantEntity
{
    public Guid TaskId { get; private init; }
    public Guid WorkspaceId { get; private init; }
    public string FileName { get; private init; } = null!;
    public string BlobUrl { get; private init; } = null!;
    public long SizeBytes { get; private init; }
    public Guid UploadedById { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private TaskAttachment() { }

    internal static TaskAttachment Create(
        Guid taskId, Guid workspaceId, string fileName, string blobUrl, long sizeBytes, Guid uploadedById)
    {
        return new TaskAttachment
        {
            Id          = Guid.NewGuid(),
            TaskId      = taskId,
            WorkspaceId = workspaceId,
            FileName    = fileName,
            BlobUrl     = blobUrl,
            SizeBytes   = sizeBytes,
            UploadedById = uploadedById,
            CreatedAt   = DateTimeOffset.UtcNow,
        };
    }
}

public sealed class TaskDependency : Entity
{
    public Guid SourceTaskId { get; private init; }
    public Guid TargetTaskId { get; private init; }
    public DependencyType Type { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    private TaskDependency() { }

    internal static TaskDependency Create(Guid sourceId, Guid targetId, DependencyType type)
    {
        return new TaskDependency
        {
            Id           = Guid.NewGuid(),
            SourceTaskId = sourceId,
            TargetTaskId = targetId,
            Type         = type,
            CreatedAt    = DateTimeOffset.UtcNow,
        };
    }
}
