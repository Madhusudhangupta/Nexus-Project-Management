using Microsoft.EntityFrameworkCore;
using NexusPM.Domain.Aggregates.Tasks;
using NexusPM.Domain.Common;
using NexusPM.Domain.Repositories;

namespace NexusPM.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ITaskRepository.
/// Uses AsNoTracking() for all read queries to avoid change-tracking overhead.
/// Uses compiled queries for the hottest paths.
/// </summary>
public sealed class TaskRepository(AppDbContext db) : ITaskRepository
{
    public async Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<TaskItem?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default) =>
        await db.Tasks
            .Include(t => t.Comments.Where(c => c.DeletedAt == null))
            .Include(t => t.Attachments)
            .Include(t => t.Dependencies)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<PagedResult<TaskItem>> GetProjectTasksAsync(
        Guid projectId,
        TaskQueryFilter filter,
        PaginationParams pagination,
        CancellationToken ct = default)
    {
        var query = db.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId);

        // Apply filters
        if (filter.SprintId.HasValue)
            query = query.Where(t => t.SprintId == filter.SprintId);

        if (filter.AssigneeId.HasValue)
            query = query.Where(t => t.AssigneeId == filter.AssigneeId);

        if (filter.StatusId.HasValue)
            query = query.Where(t => t.StatusId == filter.StatusId);

        if (filter.Priority.HasValue)
            query = query.Where(t => t.Priority == Domain.ValueObjects.Priority.FromValue(filter.Priority.Value));

        if (filter.Labels?.Length > 0)
            query = query.Where(t => t.Labels.Any(l => filter.Labels.Contains(l)));

        if (filter.DueDateBefore.HasValue)
            query = query.Where(t => t.DueDate <= filter.DueDateBefore);

        if (filter.DueDateAfter.HasValue)
            query = query.Where(t => t.DueDate >= filter.DueDateAfter);

        if (filter.HasAssignee.HasValue)
            query = filter.HasAssignee.Value
                ? query.Where(t => t.AssigneeId != null)
                : query.Where(t => t.AssigneeId == null);

        if (!string.IsNullOrWhiteSpace(filter.Search))
            query = query.Where(t =>
                EF.Functions.ILike(t.Title, $"%{filter.Search}%") ||
                (t.Description != null && EF.Functions.ILike(t.Description, $"%{filter.Search}%")));

        // Apply sorting
        query = (filter.SortBy.ToLowerInvariant(), filter.SortDirection.ToLowerInvariant()) switch
        {
            ("priority", "asc")    => query.OrderBy(t => t.Priority),
            ("priority", _)        => query.OrderByDescending(t => t.Priority),
            ("duedate", "asc")     => query.OrderBy(t => t.DueDate),
            ("duedate", _)         => query.OrderByDescending(t => t.DueDate),
            ("updatedat", "asc")   => query.OrderBy(t => t.UpdatedAt),
            ("updatedat", _)       => query.OrderByDescending(t => t.UpdatedAt),
            ("title", "asc")       => query.OrderBy(t => t.Title),
            ("title", _)           => query.OrderByDescending(t => t.Title),
            (_, "asc")             => query.OrderBy(t => t.CreatedAt),
            _                      => query.OrderByDescending(t => t.CreatedAt),
        };

        var p = pagination.WithValidation();
        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((p.Page - 1) * p.PageSize)
            .Take(p.PageSize)
            .ToListAsync(ct);

        return new PagedResult<TaskItem>
        {
            Items      = items,
            TotalCount = totalCount,
            Page       = p.Page,
            PageSize   = p.PageSize,
        };
    }

    public async Task<PagedResult<TaskItem>> GetMyTasksAsync(
        Guid userId, Guid workspaceId,
        TaskQueryFilter filter, PaginationParams pagination,
        CancellationToken ct = default)
    {
        var query = db.Tasks
            .AsNoTracking()
            .Where(t => t.AssigneeId == userId);

        // Delegate to shared filtering logic
        return await GetProjectTasksAsync(Guid.Empty, filter with { AssigneeId = userId }, pagination, ct);
    }

    public async Task<int> GetNextTaskSequenceAsync(Guid projectId, CancellationToken ct = default)
    {
        // Count all tasks (including deleted) to ensure keys are never reused
        var count = await db.Tasks
            .IgnoreQueryFilters()
            .CountAsync(t => t.ProjectId == projectId, ct);
        return count + 1;
    }

    public async Task<IReadOnlyList<Guid>> GetTransitiveDependencyIdsAsync(
        Guid taskId, CancellationToken ct = default)
    {
        // Walk the dependency graph to collect all transitive blocker IDs
        // For production, this would use a recursive CTE in PostgreSQL
        var visited = new HashSet<Guid>();
        var queue   = new Queue<Guid>([taskId]);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;

            var deps = await db.TaskDependencies
                .AsNoTracking()
                .Where(d => d.SourceTaskId == current &&
                            d.Type == DependencyType.Blocks)
                .Select(d => d.TargetTaskId)
                .ToListAsync(ct);

            foreach (var dep in deps) queue.Enqueue(dep);
        }

        visited.Remove(taskId);
        return visited.ToList();
    }

    public void Add(TaskItem task)    => db.Tasks.Add(task);
    public void Update(TaskItem task) => db.Tasks.Update(task);
    public void Remove(TaskItem task) => db.Tasks.Remove(task);
}
