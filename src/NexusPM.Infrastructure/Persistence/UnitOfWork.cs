using NexusPM.Domain.Repositories;
using NexusPM.Infrastructure.Persistence.Repositories;

namespace NexusPM.Infrastructure.Persistence;

/// <summary>
/// Coordinates all repository operations within a single DbContext transaction.
/// Repositories share the same DbContext instance, so all changes are saved
/// atomically when SaveChangesAsync is called.
/// </summary>
public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    private ITaskRepository?      _tasks;
    private IProjectRepository?   _projects;
    private IWorkspaceRepository? _workspaces;
    private IUserRepository?      _users;

    public ITaskRepository      Tasks      => _tasks      ??= new TaskRepository(db);
    public IProjectRepository   Projects   => _projects   ??= new ProjectRepository(db);
    public IWorkspaceRepository Workspaces => _workspaces ??= new WorkspaceRepository(db);
    public IUserRepository      Users      => _users      ??= new UserRepository(db);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        db.SaveChangesAsync(ct);
}
