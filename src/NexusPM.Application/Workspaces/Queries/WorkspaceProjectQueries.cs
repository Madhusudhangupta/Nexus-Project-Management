using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;

// ── Workspace Queries ─────────────────────────────────────────────────────────

namespace NexusPM.Application.Workspaces.Queries
{

public sealed record WorkspaceDto(
    Guid   WorkspaceId,
    string Name,
    string Slug,
    string? LogoUrl,
    string Tier,
    int    MemberCount,
    int    ProjectCount,
    DateTimeOffset CreatedAt);

public sealed record WorkspaceMemberDto(
    Guid   UserId,
    string DisplayName,
    string Email,
    string? AvatarUrl,
    string Role,
    DateTimeOffset? JoinedAt);

// Get single workspace
public sealed record GetWorkspaceQuery(Guid WorkspaceId) : IQuery<WorkspaceDto>, ICacheable
{
    public string   CacheKey      => $"workspace:{WorkspaceId}:detail";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(10);
    public bool     BypassCache   => false;
}

public sealed class GetWorkspaceQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetWorkspaceQuery, WorkspaceDto>
{
    public async Task<WorkspaceDto> Handle(GetWorkspaceQuery query, CancellationToken ct)
    {
        var workspace = await uow.Workspaces.GetByIdWithMembersAsync(query.WorkspaceId, ct)
            ?? throw new NotFoundException("Workspace", query.WorkspaceId);

        return new WorkspaceDto(
            workspace.Id,
            workspace.Name,
            workspace.Slug.Value,
            workspace.LogoUrl,
            workspace.Tier.ToString(),
            workspace.Memberships.Count,
            ProjectCount: 0, // populated separately from project repository
            workspace.CreatedAt);
    }
}

// Get all workspaces for current user
public sealed record GetWorkspacesQuery : IQuery<IReadOnlyList<WorkspaceDto>> { }

public sealed class GetWorkspacesQueryHandler(IUnitOfWork uow, ICurrentUser currentUser)
    : IRequestHandler<GetWorkspacesQuery, IReadOnlyList<WorkspaceDto>>
{
    public async Task<IReadOnlyList<WorkspaceDto>> Handle(GetWorkspacesQuery query, CancellationToken ct)
    {
        var workspaces = await uow.Workspaces.GetUserWorkspacesAsync(currentUser.UserId, ct);
        return workspaces
            .Select(w => new WorkspaceDto(
                w.Id, w.Name, w.Slug.Value, w.LogoUrl,
                w.Tier.ToString(), w.Memberships.Count, 0, w.CreatedAt))
            .ToList();
    }
}

// Get workspace members
public sealed record GetMembersQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<WorkspaceMemberDto>> { }

public sealed class GetMembersQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetMembersQuery, IReadOnlyList<WorkspaceMemberDto>>
{
    public async Task<IReadOnlyList<WorkspaceMemberDto>> Handle(GetMembersQuery query, CancellationToken ct)
    {
        var workspace = await uow.Workspaces.GetByIdWithMembersAsync(query.WorkspaceId, ct)
            ?? throw new NotFoundException("Workspace", query.WorkspaceId);

        // In production, this would be a JOIN query on the read side
        var result = new List<WorkspaceMemberDto>();
        foreach (var m in workspace.Memberships)
        {
            var user = await uow.Users.GetByIdAsync(m.UserId, ct);
            if (user is null) continue;
            result.Add(new WorkspaceMemberDto(
                user.Id, user.DisplayName, user.Email.Value,
                user.AvatarUrl, m.Role.ToString(), m.JoinedAt));
        }
        return result;
    }
}
}

// ── Project Queries ───────────────────────────────────────────────────────────

namespace NexusPM.Application.Projects.Queries
{

public sealed record ProjectDto(
    Guid   ProjectId,
    Guid   WorkspaceId,
    string Name,
    string Key,
    string? Description,
    bool   IsArchived,
    int    TaskCount,
    IReadOnlyList<WorkflowStateDto> WorkflowStates,
    DateTimeOffset CreatedAt);

public sealed record WorkflowStateDto(
    Guid   StateId,
    string Name,
    string Color,
    bool   IsTerminal,
    int    Position);

public sealed record GetProjectQuery(Guid WorkspaceId, Guid ProjectId)
    : IQuery<ProjectDto>, ICacheable
{
    public string   CacheKey      => $"project:{ProjectId}:detail";
    public TimeSpan CacheDuration => TimeSpan.FromMinutes(15);
    public bool     BypassCache   => false;
}

public sealed class GetProjectQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetProjectQuery, ProjectDto>
{
    public async Task<ProjectDto> Handle(GetProjectQuery query, CancellationToken ct)
    {
        var project = await uow.Projects.GetByIdWithStatesAsync(query.ProjectId, ct)
            ?? throw new NotFoundException("Project", query.ProjectId);

        if (project.WorkspaceId != query.WorkspaceId)
            throw new UnauthorizedException("Project does not belong to this workspace.");

        return new ProjectDto(
            project.Id,
            project.WorkspaceId,
            project.Name,
            project.Key,
            project.Description,
            project.IsArchived,
            TaskCount: 0,
            project.WorkflowStates
                .OrderBy(s => s.Position)
                .Select(s => new WorkflowStateDto(s.Id, s.Name, s.Color, s.IsTerminal, s.Position))
                .ToList(),
            project.CreatedAt);
    }
}

public sealed record GetProjectsQuery(Guid WorkspaceId) : IQuery<IReadOnlyList<ProjectDto>> { }

public sealed class GetProjectsQueryHandler(IUnitOfWork uow)
    : IRequestHandler<GetProjectsQuery, IReadOnlyList<ProjectDto>>
{
    public async Task<IReadOnlyList<ProjectDto>> Handle(GetProjectsQuery query, CancellationToken ct)
    {
        var projects = await uow.Projects.GetWorkspaceProjectsAsync(query.WorkspaceId, ct);
        return projects
            .Select(p => new ProjectDto(
                p.Id, p.WorkspaceId, p.Name, p.Key, p.Description, p.IsArchived, 0,
                p.WorkflowStates.OrderBy(s => s.Position)
                    .Select(s => new WorkflowStateDto(s.Id, s.Name, s.Color, s.IsTerminal, s.Position))
                    .ToList(),
                p.CreatedAt))
            .ToList();
    }
}

}