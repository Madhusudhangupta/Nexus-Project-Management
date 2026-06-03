using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Common;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;

namespace NexusPM.Application.Tasks.Queries.GetTask;

// ── DTOs ──────────────────────────────────────────────────────────────────────

public sealed record TaskDetailDto
{
    public Guid TaskId { get; init; }
    public string TaskKey { get; init; } = null!;
    public string Title { get; init; } = null!;
    public string? Description { get; init; }
    public Guid StatusId { get; init; }
    public string StatusName { get; init; } = null!;
    public string StatusColor { get; init; } = null!;
    public int Priority { get; init; }
    public string PriorityLabel { get; init; } = null!;
    public Guid? AssigneeId { get; init; }
    public string? AssigneeName { get; init; }
    public string? AssigneeAvatarUrl { get; init; }
    public Guid ReporterId { get; init; }
    public string ReporterName { get; init; } = null!;
    public int? StoryPoints { get; init; }
    public DateOnly? DueDate { get; init; }
    public decimal? EstimatedHours { get; init; }
    public decimal LoggedHours { get; init; }
    public string[] Labels { get; init; } = [];
    public Guid? SprintId { get; init; }
    public string? SprintName { get; init; }
    public int CommentCount { get; init; }
    public int AttachmentCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public IReadOnlyList<CommentDto> Comments { get; init; } = [];
}

public sealed record CommentDto(
    Guid CommentId,
    string Content,
    Guid AuthorId,
    string AuthorName,
    string? AuthorAvatarUrl,
    bool IsEdited,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TaskSummaryDto
{
    public Guid TaskId { get; init; }
    public string TaskKey { get; init; } = null!;
    public string Title { get; init; } = null!;
    public Guid StatusId { get; init; }
    public string StatusName { get; init; } = null!;
    public string StatusColor { get; init; } = null!;
    public int Priority { get; init; }
    public Guid? AssigneeId { get; init; }
    public string? AssigneeName { get; init; }
    public string? AssigneeAvatarUrl { get; init; }
    public int? StoryPoints { get; init; }
    public DateOnly? DueDate { get; init; }
    public string[] Labels { get; init; } = [];
    public int CommentCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

// ── Get Single Task ───────────────────────────────────────────────────────────

public sealed record GetTaskQuery(Guid WorkspaceId, Guid TaskId)
    : IQuery<TaskDetailDto>, ICacheable
{
    public string CacheKey     => $"task:{TaskId}:detail";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(5);
    public bool BypassCache    => false;
}

public sealed class GetTaskQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetTaskQuery, TaskDetailDto>
{
    public async Task<TaskDetailDto> Handle(GetTaskQuery query, CancellationToken ct)
    {
        var task = await uow.Tasks.GetByIdWithDetailsAsync(query.TaskId, ct)
            ?? throw new NotFoundException("Task", query.TaskId);

        if (task.WorkspaceId != query.WorkspaceId)
            throw new UnauthorizedException("Task does not belong to this workspace.");

        // Map to DTO — in production this would use a projection query from the repository
        return new TaskDetailDto
        {
            TaskId       = task.Id,
            TaskKey      = task.TaskKey,
            Title        = task.Title,
            Description  = task.Description,
            StatusId     = task.StatusId,
            Priority     = task.Priority.Value,
            PriorityLabel = task.Priority.Label,
            AssigneeId   = task.AssigneeId,
            ReporterId   = task.ReporterId,
            StoryPoints  = task.StoryPoints?.Value,
            DueDate      = task.DueDate,
            EstimatedHours = task.EstimatedHours,
            LoggedHours  = task.LoggedHours,
            Labels       = [.. task.Labels],
            SprintId     = task.SprintId,
            CommentCount = task.Comments.Count(c => !c.DeletedAt.HasValue),
            AttachmentCount = task.Attachments.Count,
            CreatedAt    = task.CreatedAt,
            UpdatedAt    = task.UpdatedAt,
            Comments     = task.Comments
                .Where(c => !c.DeletedAt.HasValue)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new CommentDto(
                    c.Id, c.Content, c.AuthorId, "Unknown", null,
                    c.IsEdited, c.CreatedAt, c.UpdatedAt))
                .ToList(),
        };
    }
}

// ── Get Project Tasks ─────────────────────────────────────────────────────────

public sealed record GetProjectTasksQuery : IQuery<PagedResult<TaskSummaryDto>>
{
    public required Guid WorkspaceId { get; init; }
    public required Guid ProjectId { get; init; }
    public Guid? SprintId { get; init; }
    public Guid? AssigneeId { get; init; }
    public Guid? StatusId { get; init; }
    public int? Priority { get; init; }
    public string[]? Labels { get; init; }
    public string? Search { get; init; }
    public DateOnly? DueDateBefore { get; init; }
    public DateOnly? DueDateAfter { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string SortBy { get; init; } = "createdAt";
    public string SortDirection { get; init; } = "desc";
}

public sealed class GetProjectTasksQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetProjectTasksQuery, PagedResult<TaskSummaryDto>>
{
    public async Task<PagedResult<TaskSummaryDto>> Handle(GetProjectTasksQuery query, CancellationToken ct)
    {
        var filter = new TaskQueryFilter
        {
            SprintId       = query.SprintId,
            AssigneeId     = query.AssigneeId,
            StatusId       = query.StatusId,
            Priority       = query.Priority,
            Labels         = query.Labels,
            Search         = query.Search,
            DueDateBefore  = query.DueDateBefore,
            DueDateAfter   = query.DueDateAfter,
            SortBy         = query.SortBy,
            SortDirection  = query.SortDirection,
        };

        var pagination = new PaginationParams
        {
            Page     = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100),
        };

        var result = await uow.Tasks.GetProjectTasksAsync(query.ProjectId, filter, pagination, ct);

        return new PagedResult<TaskSummaryDto>
        {
            Items      = result.Items.Select(MapToSummary).ToList(),
            TotalCount = result.TotalCount,
            Page       = result.Page,
            PageSize   = result.PageSize,
        };
    }

    private static TaskSummaryDto MapToSummary(Domain.Aggregates.Tasks.TaskItem t) =>
        new()
        {
            TaskId        = t.Id,
            TaskKey       = t.TaskKey,
            Title         = t.Title,
            StatusId      = t.StatusId,
            Priority      = t.Priority.Value,
            AssigneeId    = t.AssigneeId,
            StoryPoints   = t.StoryPoints?.Value,
            DueDate       = t.DueDate,
            Labels        = [.. t.Labels],
            CommentCount  = t.Comments.Count(c => !c.DeletedAt.HasValue),
            CreatedAt     = t.CreatedAt,
            UpdatedAt     = t.UpdatedAt,
        };
}
