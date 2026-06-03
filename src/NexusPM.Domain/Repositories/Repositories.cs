using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Aggregates.Tasks;
using NexusPM.Domain.Aggregates.Users;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Common;

namespace NexusPM.Domain.Repositories;

// ── Task Repository ───────────────────────────────────────────────────────────

/// <summary>Filter parameters for task list queries.</summary>
public sealed record TaskQueryFilter
{
    public Guid? SprintId { get; init; }
    public Guid? AssigneeId { get; init; }
    public Guid? StatusId { get; init; }
    public int? Priority { get; init; }
    public string[]? Labels { get; init; }
    public DateOnly? DueDateBefore { get; init; }
    public DateOnly? DueDateAfter { get; init; }
    public bool? HasAssignee { get; init; }
    public string? Search { get; init; }
    public string SortBy { get; init; } = "createdAt";
    public string SortDirection { get; init; } = "desc";
}

public sealed record PaginationParams
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;

    public static PaginationParams Default => new();

    public PaginationParams WithValidation()
    {
        var page = Math.Max(1, Page);
        var size = Math.Clamp(PageSize, 1, 100);
        return new PaginationParams { Page = page, PageSize = size };
    }
}

public interface ITaskRepository
{
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<TaskItem?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<TaskItem>> GetProjectTasksAsync(
        Guid projectId, TaskQueryFilter filter, PaginationParams pagination, CancellationToken ct = default);
    Task<PagedResult<TaskItem>> GetMyTasksAsync(
        Guid userId, Guid workspaceId, TaskQueryFilter filter, PaginationParams pagination, CancellationToken ct = default);
    Task<int> GetNextTaskSequenceAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetTransitiveDependencyIdsAsync(Guid taskId, CancellationToken ct = default);
    void Add(TaskItem task);
    void Update(TaskItem task);
    void Remove(TaskItem task);
}

// ── Project Repository ────────────────────────────────────────────────────────

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Project?> GetByIdWithStatesAsync(Guid id, CancellationToken ct = default);
    Task<Project?> GetByKeyAsync(Guid workspaceId, string key, CancellationToken ct = default);
    Task<IReadOnlyList<Project>> GetWorkspaceProjectsAsync(Guid workspaceId, CancellationToken ct = default);
    Task<bool> KeyExistsAsync(Guid workspaceId, string key, CancellationToken ct = default);
    void Add(Project project);
    void Update(Project project);
}

// ── Workspace Repository ──────────────────────────────────────────────────────

public interface IWorkspaceRepository
{
    Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Workspace?> GetByIdWithMembersAsync(Guid id, CancellationToken ct = default);
    Task<Workspace?> GetBySlugAsync(string slug, CancellationToken ct = default);
    Task<IReadOnlyList<Workspace>> GetUserWorkspacesAsync(Guid userId, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task<WorkspaceMembership?> GetPrimaryMembershipAsync(Guid userId, CancellationToken ct = default);
    void Add(Workspace workspace);
    void Update(Workspace workspace);
}

// ── User Repository ───────────────────────────────────────────────────────────

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default);
    Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken ct = default);
    void AddRefreshToken(RefreshToken token);
    void Add(User user);
    void Update(User user);
}

// ── Unit of Work ──────────────────────────────────────────────────────────────

/// <summary>
/// Coordinates transactional writes across multiple repositories.
/// A single SaveChangesAsync call commits all pending changes and
/// dispatches all accumulated domain events.
/// </summary>
public interface IUnitOfWork
{
    ITaskRepository Tasks { get; }
    IProjectRepository Projects { get; }
    IWorkspaceRepository Workspaces { get; }
    IUserRepository Users { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
