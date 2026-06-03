using FluentValidation;
using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;

// ── Create Project ────────────────────────────────────────────────────────────

namespace NexusPM.Application.Projects.Commands.CreateProject
{

public sealed record CreateProjectCommand : ICommand<CreateProjectResult>
{
    public required Guid   WorkspaceId { get; init; }
    public required string Name        { get; init; }
    public required string Key         { get; init; }
    public string?         Description { get; init; }
}

public sealed record CreateProjectResult(Guid ProjectId, string Key);

public sealed class CreateProjectCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser)
    : IRequestHandler<CreateProjectCommand, CreateProjectResult>
{
    public async Task<CreateProjectResult> Handle(CreateProjectCommand cmd, CancellationToken ct)
    {
        if (!currentUser.IsInRole("Admin"))
            throw new UnauthorizedException("Only workspace admins can create projects.");

        // Validate workspace exists
        var workspace = await uow.Workspaces.GetByIdAsync(cmd.WorkspaceId, ct)
            ?? throw new NotFoundException("Workspace", cmd.WorkspaceId);

        var key = cmd.Key.Trim().ToUpperInvariant();
        if (await uow.Projects.KeyExistsAsync(cmd.WorkspaceId, key, ct))
            throw new DuplicateException("Project", "key", key);

        var project = Project.Create(
            cmd.WorkspaceId, cmd.Name, key, cmd.Description, currentUser.UserId);

        uow.Projects.Add(project);
        await uow.SaveChangesAsync(ct);

        return new CreateProjectResult(project.Id, project.Key);
    }
}

public sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Project name is required.")
            .MaximumLength(200);

        RuleFor(x => x.Key)
            .NotEmpty().WithMessage("Project key is required.")
            .Length(2, 10).WithMessage("Project key must be 2-10 characters.")
            .Matches(@"^[A-Za-z0-9]+$").WithMessage("Project key must contain only letters and digits.");

        RuleFor(x => x.Description)
            .MaximumLength(10_000).When(x => x.Description is not null);
    }
}
}

// ── Update Project ────────────────────────────────────────────────────────────

namespace NexusPM.Application.Projects.Commands.UpdateProject
{

public sealed record UpdateProjectCommand : ICommand<UpdateProjectResult>
{
    public required Guid   WorkspaceId { get; init; }
    public required Guid   ProjectId   { get; init; }
    public required string Name        { get; init; }
    public string?         Description { get; init; }
}

public sealed record UpdateProjectResult(Guid ProjectId, DateTimeOffset UpdatedAt);

public sealed class UpdateProjectCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache)
    : IRequestHandler<UpdateProjectCommand, UpdateProjectResult>
{
    public async Task<UpdateProjectResult> Handle(UpdateProjectCommand cmd, CancellationToken ct)
    {
        var project = await uow.Projects.GetByIdAsync(cmd.ProjectId, ct)
            ?? throw new NotFoundException("Project", cmd.ProjectId);

        if (project.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Project does not belong to this workspace.");

        project.Update(cmd.Name, cmd.Description, currentUser.UserId);
        uow.Projects.Update(project);
        await uow.SaveChangesAsync(ct);

        await cache.RemoveAsync($"project:{cmd.ProjectId}:detail", ct);

        return new UpdateProjectResult(cmd.ProjectId, project.UpdatedAt);
    }
}

public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.ProjectId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
}

// ── Delete Project ────────────────────────────────────────────────────────────

namespace NexusPM.Application.Projects.Commands.DeleteProject
{

public sealed record DeleteProjectCommand : ICommand
{
    public required Guid WorkspaceId { get; init; }
    public required Guid ProjectId   { get; init; }
}

public sealed class DeleteProjectCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache)
    : IRequestHandler<DeleteProjectCommand>
{
    public async Task Handle(DeleteProjectCommand cmd, CancellationToken ct)
    {
        if (!currentUser.IsInRole("Admin"))
            throw new UnauthorizedException("Only workspace admins can delete projects.");

        var project = await uow.Projects.GetByIdAsync(cmd.ProjectId, ct)
            ?? throw new NotFoundException("Project", cmd.ProjectId);

        if (project.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Project does not belong to this workspace.");

        project.Archive(currentUser.UserId);
        uow.Projects.Update(project);
        await uow.SaveChangesAsync(ct);

        await cache.RemoveAsync($"project:{cmd.ProjectId}:detail", ct);
        await cache.RemoveByPrefixAsync($"projects:{cmd.WorkspaceId}", ct);
    }
}

}