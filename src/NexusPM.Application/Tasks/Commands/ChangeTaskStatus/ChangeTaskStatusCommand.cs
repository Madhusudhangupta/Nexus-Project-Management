using FluentValidation;
using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;

namespace NexusPM.Application.Tasks.Commands.ChangeTaskStatus;

public sealed record ChangeTaskStatusCommand : ICommand<ChangeTaskStatusResult>
{
    public required Guid WorkspaceId { get; init; }
    public required Guid TaskId { get; init; }
    public required Guid NewStatusId { get; init; }
}

public sealed record ChangeTaskStatusResult(Guid TaskId, Guid NewStatusId, string NewStatusName);

public sealed class ChangeTaskStatusCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache,
    IRealtimeService realtime)
    : IRequestHandler<ChangeTaskStatusCommand, ChangeTaskStatusResult>
{
    public async Task<ChangeTaskStatusResult> Handle(ChangeTaskStatusCommand cmd, CancellationToken ct)
    {
        var task = await uow.Tasks.GetByIdAsync(cmd.TaskId, ct)
            ?? throw new NotFoundException("Task", cmd.TaskId);

        if (task.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Task does not belong to this workspace.");

        var project = await uow.Projects.GetByIdWithStatesAsync(task.ProjectId, ct)
            ?? throw new NotFoundException("Project", task.ProjectId);

        var newState = project.GetWorkflowState(cmd.NewStatusId);

        task.ChangeStatus(cmd.NewStatusId, currentUser.UserId);
        uow.Tasks.Update(task);
        await uow.SaveChangesAsync(ct);

        // Invalidate caches
        await cache.RemoveAsync($"task:{cmd.TaskId}:detail", ct);
        await cache.RemoveByPrefixAsync($"tasks:{cmd.WorkspaceId}:{task.ProjectId}", ct);

        // Push real-time update
        await realtime.SendToWorkspaceAsync(cmd.WorkspaceId, "TaskStatusChanged", new
        {
            taskId      = cmd.TaskId,
            taskKey     = task.TaskKey,
            newStatusId = cmd.NewStatusId,
            newStatusName = newState.Name,
            updatedById = currentUser.UserId,
        }, ct);

        return new ChangeTaskStatusResult(cmd.TaskId, cmd.NewStatusId, newState.Name);
    }
}

public sealed class ChangeTaskStatusCommandValidator : AbstractValidator<ChangeTaskStatusCommand>
{
    public ChangeTaskStatusCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.NewStatusId).NotEmpty().WithMessage("NewStatusId is required.");
    }
}
