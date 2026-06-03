using FluentValidation;
using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Aggregates.Tasks;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;
using NexusPM.Domain.ValueObjects;

namespace NexusPM.Application.Tasks.Commands.CreateTask;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// Creates a new task within a project.
/// The handler validates project membership, resolves the default workflow state,
/// generates a sequential task key (e.g. NEXUS-42), and persists the task.
/// </summary>
public sealed record CreateTaskCommand : ICommand<CreateTaskResult>
{
    public required Guid WorkspaceId { get; init; }
    public required Guid ProjectId { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public Guid? AssigneeId { get; init; }
    public DateOnly? DueDate { get; init; }
    public int Priority { get; init; } = 3;
    public int? StoryPoints { get; init; }
    public string[] Labels { get; init; } = [];
    public Guid? SprintId { get; init; }
    public Guid? ParentTaskId { get; init; }
}

public sealed record CreateTaskResult(Guid TaskId, string TaskKey);

// ── Handler ───────────────────────────────────────────────────────────────────

public sealed class CreateTaskCommandHandler(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ICacheService cache)
    : IRequestHandler<CreateTaskCommand, CreateTaskResult>
{
    public async Task<CreateTaskResult> Handle(CreateTaskCommand cmd, CancellationToken ct)
    {
        // Load project and validate it belongs to the workspace
        var project = await uow.Projects.GetByIdWithStatesAsync(cmd.ProjectId, ct)
            ?? throw new NotFoundException("Project", cmd.ProjectId);

        if (project.WorkspaceId != cmd.WorkspaceId)
            throw new UnauthorizedException("Project does not belong to this workspace.");

        if (project.IsArchived)
            throw new BusinessRuleViolationException("ArchivedProject", "Cannot add tasks to an archived project.");

        // Get default (first) workflow state
        var defaultState = project.GetDefaultState();

        // Generate sequential task key: NEXUS-1, NEXUS-2, ...
        var sequence = await uow.Tasks.GetNextTaskSequenceAsync(cmd.ProjectId, ct);
        var taskKey  = $"{project.Key}-{sequence}";

        // Build the task
        var priority = Priority.FromValue(cmd.Priority);
        var task = TaskItem.Create(
            cmd.WorkspaceId,
            cmd.ProjectId,
            taskKey,
            cmd.Title,
            defaultState.Id,
            currentUser.UserId,
            priority);

        if (!string.IsNullOrWhiteSpace(cmd.Description))
            task.UpdateDescription(cmd.Description, currentUser.UserId);

        if (cmd.AssigneeId.HasValue)
            task.Assign(cmd.AssigneeId, currentUser.UserId);

        if (cmd.DueDate.HasValue)
            task.SetDueDate(cmd.DueDate, currentUser.UserId);

        if (cmd.StoryPoints.HasValue)
            task.SetStoryPoints(cmd.StoryPoints, currentUser.UserId);

        if (cmd.Labels.Length > 0)
            task.SetLabels(cmd.Labels, currentUser.UserId);

        if (cmd.SprintId.HasValue)
            task.AssignToSprint(cmd.SprintId, currentUser.UserId);

        uow.Tasks.Add(task);
        await uow.SaveChangesAsync(ct);

        // Invalidate project task list cache
        await cache.RemoveByPrefixAsync($"tasks:{cmd.WorkspaceId}:{cmd.ProjectId}", ct);

        return new CreateTaskResult(task.Id, task.TaskKey);
    }
}

// ── Validator ─────────────────────────────────────────────────────────────────

public sealed class CreateTaskCommandValidator : AbstractValidator<CreateTaskCommand>
{
    private static readonly int[] FibonacciPoints = [0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89];

    public CreateTaskCommandValidator()
    {
        RuleFor(x => x.WorkspaceId)
            .NotEmpty().WithMessage("WorkspaceId is required.");

        RuleFor(x => x.ProjectId)
            .NotEmpty().WithMessage("ProjectId is required.");

        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(500).WithMessage("Title must not exceed 500 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(100_000).When(x => x.Description is not null);

        RuleFor(x => x.Priority)
            .InclusiveBetween(1, 5)
            .WithMessage("Priority must be between 1 (Critical) and 5 (None).");

        RuleFor(x => x.StoryPoints)
            .Must(sp => sp is null || FibonacciPoints.Contains(sp.Value))
            .WithMessage($"Story points must be one of: {string.Join(", ", FibonacciPoints)}.");

        RuleFor(x => x.DueDate)
            .Must(d => d is null || d.Value >= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Due date must be today or in the future.");

        RuleFor(x => x.Labels)
            .Must(l => l.Length <= 20)
            .WithMessage("A task cannot have more than 20 labels.")
            .ForEach(label => label.MaximumLength(50));
    }
}
