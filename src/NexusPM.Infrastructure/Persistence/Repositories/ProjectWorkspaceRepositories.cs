using Microsoft.EntityFrameworkCore;
using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Aggregates.Users;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Repositories;

namespace NexusPM.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Project?> GetByIdWithStatesAsync(Guid id, CancellationToken ct = default) =>
        await db.Projects
            .Include(p => p.WorkflowStates)
            .Include(p => p.Sprints)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Project?> GetByKeyAsync(Guid workspaceId, string key, CancellationToken ct = default) =>
        await db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId &&
                                      p.Key == key.ToUpperInvariant(), ct);

    public async Task<IReadOnlyList<Project>> GetWorkspaceProjectsAsync(
        Guid workspaceId, CancellationToken ct = default) =>
        await db.Projects
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> KeyExistsAsync(Guid workspaceId, string key, CancellationToken ct = default) =>
        await db.Projects.AnyAsync(
            p => p.WorkspaceId == workspaceId && p.Key == key.ToUpperInvariant(), ct);

    public void Add(Project project)    => db.Projects.Add(project);
    public void Update(Project project) => db.Projects.Update(project);
}

public sealed class WorkspaceRepository(AppDbContext db) : IWorkspaceRepository
{
    public async Task<Workspace?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<Workspace?> GetByIdWithMembersAsync(Guid id, CancellationToken ct = default) =>
        await db.Workspaces
            .Include(w => w.Memberships)
            .FirstOrDefaultAsync(w => w.Id == id, ct);

    public async Task<Workspace?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
        await db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Slug == Domain.ValueObjects.WorkspaceSlug.Create(slug), ct);

    public async Task<IReadOnlyList<Workspace>> GetUserWorkspacesAsync(
        Guid userId, CancellationToken ct = default) =>
        await db.Workspaces
            .AsNoTracking()
            .Where(w => w.Memberships.Any(m => m.UserId == userId))
            .OrderBy(w => w.Name)
            .ToListAsync(ct);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default) =>
        await db.Workspaces.AnyAsync(w => w.Slug == Domain.ValueObjects.WorkspaceSlug.Create(slug), ct);

    public async Task<WorkspaceMembership?> GetPrimaryMembershipAsync(Guid userId, CancellationToken ct = default) =>
        await db.Set<WorkspaceMembership>().FirstOrDefaultAsync(m => m.UserId == userId, ct);

    public void Add(Workspace workspace)    => db.Workspaces.Add(workspace);
    public void Update(Workspace workspace) => db.Workspaces.Update(workspace);
}

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.Normalized == normalizedEmail, ct);

    public async Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct = default) =>
        await db.Users.AnyAsync(u => u.Email.Normalized == normalizedEmail, ct);

    public async Task<RefreshToken?> GetRefreshTokenAsync(string tokenHash, CancellationToken ct = default) =>
        await db.Set<RefreshToken>()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash, ct);

    public void AddRefreshToken(RefreshToken token) =>
        db.Set<RefreshToken>().Add(token);

    public void Add(User user)    => db.Users.Add(user);
    public void Update(User user) => db.Users.Update(user);
}
