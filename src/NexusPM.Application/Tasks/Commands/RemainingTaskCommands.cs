using FluentValidation;
using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;

// ── Assign Task ───────────────────────────────────────────────────────────────

namespace NexusPM.Application.Tasks.Commands.AssignTask
{

public sealed record AssignTaskCommand : ICommand<AssignTaskResult>
{
    public required Guid WorkspaceId { get; init; }
    public required Guid TaskId      { get; init; }
    public Guid? AssigneeId          { get; init; }  // null = unassign
}

public sealed record AssignTaskResult(Guid TaskId, Guid? AssigneeId);

public sealed class AssignTaskCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache)
    : IRequestHandler<AssignTaskCommand, AssignTaskResult>
{
    public async Task<AssignTaskResult> Handle(AssignTaskCommand cmd, CancellationToken ct)
    {
        var task = await uow.Tasks.GetByIdAsync(cmd.TaskId, ct)
            ?? throw new NotFoundException("Task", cmd.TaskId);

        if (task.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Task does not belong to this workspace.");

        // Validate the assignee is a workspace member (skip if unassigning)
        if (cmd.AssigneeId.HasValue)
        {
            var assignee = await uow.Users.GetByIdAsync(cmd.AssigneeId.Value, ct)
                ?? throw new NotFoundException("User", cmd.AssigneeId.Value);
        }

        task.Assign(cmd.AssigneeId, currentUser.UserId);
        uow.Tasks.Update(task);
        await uow.SaveChangesAsync(ct);

        await cache.RemoveAsync($"task:{cmd.TaskId}:detail", ct);

        return new AssignTaskResult(cmd.TaskId, cmd.AssigneeId);
    }
}

public sealed class AssignTaskCommandValidator : AbstractValidator<AssignTaskCommand>
{
    public AssignTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.WorkspaceId).NotEmpty();
    }
}
}

// ── Update Task ───────────────────────────────────────────────────────────────

namespace NexusPM.Application.Tasks.Commands.UpdateTask
{

public sealed record UpdateTaskCommand : ICommand<UpdateTaskResult>
{
    public required Guid   WorkspaceId      { get; init; }
    public required Guid   TaskId           { get; init; }
    public string?         Title            { get; init; }
    public string?         Description      { get; init; }
    public int?            Priority         { get; init; }
    public int?            StoryPoints      { get; init; }
    public DateOnly?       DueDate          { get; init; }
    public bool            ClearDueDate     { get; init; }
    public decimal?        EstimatedHours   { get; init; }
    public string[]?       Labels           { get; init; }
    public Guid?           SprintId         { get; init; }
    public bool            RemoveFromSprint { get; init; }
}

public sealed record UpdateTaskResult(Guid TaskId, DateTimeOffset UpdatedAt);

public sealed class UpdateTaskCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache)
    : IRequestHandler<UpdateTaskCommand, UpdateTaskResult>
{
    public async Task<UpdateTaskResult> Handle(UpdateTaskCommand cmd, CancellationToken ct)
    {
        var task = await uow.Tasks.GetByIdAsync(cmd.TaskId, ct)
            ?? throw new NotFoundException("Task", cmd.TaskId);

        if (task.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Task does not belong to this workspace.");

        if (cmd.Title is not null)
            task.UpdateTitle(cmd.Title, currentUser.UserId);

        if (cmd.Description is not null || cmd.Description == string.Empty)
            task.UpdateDescription(cmd.Description, currentUser.UserId);

        if (cmd.Priority.HasValue)
            task.SetPriority(Domain.ValueObjects.Priority.FromValue(cmd.Priority.Value), currentUser.UserId);

        if (cmd.StoryPoints.HasValue)
            task.SetStoryPoints(cmd.StoryPoints.Value, currentUser.UserId);

        if (cmd.ClearDueDate)
            task.SetDueDate(null, currentUser.UserId);
        else if (cmd.DueDate.HasValue)
            task.SetDueDate(cmd.DueDate, currentUser.UserId);

        if (cmd.Labels is not null)
            task.SetLabels(cmd.Labels, currentUser.UserId);

        if (cmd.RemoveFromSprint)
            task.AssignToSprint(null, currentUser.UserId);
        else if (cmd.SprintId.HasValue)
            task.AssignToSprint(cmd.SprintId, currentUser.UserId);

        uow.Tasks.Update(task);
        await uow.SaveChangesAsync(ct);

        await cache.RemoveAsync($"task:{cmd.TaskId}:detail", ct);
        await cache.RemoveByPrefixAsync($"tasks:{cmd.WorkspaceId}:{task.ProjectId}", ct);

        return new UpdateTaskResult(cmd.TaskId, task.UpdatedAt);
    }
}

public sealed class UpdateTaskCommandValidator : AbstractValidator<UpdateTaskCommand>
{
    private static readonly int[] FibonacciPoints = [0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89];

    public UpdateTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.WorkspaceId).NotEmpty();

        RuleFor(x => x.Title)
            .NotEmpty().When(x => x.Title is not null)
            .MaximumLength(500).When(x => x.Title is not null);

        RuleFor(x => x.Priority)
            .InclusiveBetween(1, 5).When(x => x.Priority.HasValue);

        RuleFor(x => x.StoryPoints)
            .Must(sp => sp is null || FibonacciPoints.Contains(sp.Value))
            .When(x => x.StoryPoints.HasValue)
            .WithMessage("Story points must be a Fibonacci number.");

        RuleFor(x => x.DueDate)
            .Must(d => d >= DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.DueDate.HasValue && !x.ClearDueDate)
            .WithMessage("Due date must be today or in the future.");
    }
}
}

// ── Delete Task ───────────────────────────────────────────────────────────────

namespace NexusPM.Application.Tasks.Commands.DeleteTask
{

public sealed record DeleteTaskCommand : ICommand
{
    public required Guid WorkspaceId { get; init; }
    public required Guid TaskId      { get; init; }
}

public sealed class DeleteTaskCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache)
    : IRequestHandler<DeleteTaskCommand>
{
    public async Task Handle(DeleteTaskCommand cmd, CancellationToken ct)
    {
        var task = await uow.Tasks.GetByIdAsync(cmd.TaskId, ct)
            ?? throw new NotFoundException("Task", cmd.TaskId);

        if (task.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Task does not belong to this workspace.");

        task.SoftDelete(currentUser.UserId);
        uow.Tasks.Update(task);
        await uow.SaveChangesAsync(ct);

        await cache.RemoveAsync($"task:{cmd.TaskId}:detail", ct);
        await cache.RemoveByPrefixAsync($"tasks:{cmd.WorkspaceId}:{task.ProjectId}", ct);
    }
}

public sealed class DeleteTaskCommandValidator : AbstractValidator<DeleteTaskCommand>
{
    public DeleteTaskCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.WorkspaceId).NotEmpty();
    }
}
}

// ── Add Comment ───────────────────────────────────────────────────────────────

namespace NexusPM.Application.Tasks.Commands.AddComment
{

public sealed record AddCommentCommand : ICommand<AddCommentResult>
{
    public required Guid   WorkspaceId { get; init; }
    public required Guid   TaskId      { get; init; }
    public required string Content     { get; init; }
}

public sealed record AddCommentResult(Guid CommentId, DateTimeOffset CreatedAt);

public sealed class AddCommentCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache)
    : IRequestHandler<AddCommentCommand, AddCommentResult>
{
    public async Task<AddCommentResult> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var task = await uow.Tasks.GetByIdWithDetailsAsync(cmd.TaskId, ct)
            ?? throw new NotFoundException("Task", cmd.TaskId);

        if (task.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Task does not belong to this workspace.");

        var comment = task.AddComment(cmd.Content, currentUser.UserId);
        uow.Tasks.Update(task);
        await uow.SaveChangesAsync(ct);

        await cache.RemoveAsync($"task:{cmd.TaskId}:detail", ct);

        return new AddCommentResult(comment.Id, comment.CreatedAt);
    }
}

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.TaskId).NotEmpty();
        RuleFor(x => x.WorkspaceId).NotEmpty();
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Comment content is required.")
            .MaximumLength(50_000).WithMessage("Comment must not exceed 50,000 characters.");
    }
}

}