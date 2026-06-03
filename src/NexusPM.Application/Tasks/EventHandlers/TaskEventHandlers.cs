using MediatR;
using Microsoft.Extensions.Logging;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Domain.Events.Tasks;
using NexusPM.Domain.Repositories;

namespace NexusPM.Application.Tasks.EventHandlers;

/// <summary>
/// Handles TaskAssignedEvent: publishes an integration event to RabbitMQ
/// so the worker service can send an email notification to the assignee.
/// Also pushes a real-time notification via SignalR.
/// </summary>
public sealed class TaskAssignedEventHandler(
    IMessageBus messageBus,
    IRealtimeService realtime,
    ILogger<TaskAssignedEventHandler> logger)
    : INotificationHandler<TaskAssignedEvent>
{
    public async Task Handle(TaskAssignedEvent notification, CancellationToken ct)
    {
        logger.LogInformation(
            "Task {TaskId} assigned to user {AssigneeId}",
            notification.TaskId, notification.AssigneeId);

        // Publish integration event to RabbitMQ for email delivery
        await messageBus.PublishAsync(new
        {
            EventType    = "task.assigned",
            notification.TaskId,
            notification.WorkspaceId,
            notification.ProjectId,
            notification.AssigneeId,
            notification.AssignedById,
            notification.OccurredAt,
        }, routingKey: "task.assigned", ct);

        // Push real-time notification directly to the assignee
        await realtime.SendToUserAsync(notification.AssigneeId, "NotificationReceived", new
        {
            type     = "TaskAssigned",
            taskId   = notification.TaskId,
            assignedById = notification.AssignedById,
        }, ct);
    }
}

/// <summary>
/// Handles TaskCreatedEvent: writes the audit log entry and
/// notifies workspace members in real-time.
/// </summary>
public sealed class TaskCreatedEventHandler(
    IMessageBus messageBus,
    ILogger<TaskCreatedEventHandler> logger)
    : INotificationHandler<TaskCreatedEvent>
{
    public async Task Handle(TaskCreatedEvent notification, CancellationToken ct)
    {
        logger.LogInformation(
            "Task {TaskId} created in project {ProjectId}",
            notification.TaskId, notification.ProjectId);

        await messageBus.PublishAsync(new
        {
            EventType    = "task.created",
            notification.TaskId,
            notification.WorkspaceId,
            notification.ProjectId,
            notification.ReporterId,
            notification.OccurredAt,
        }, routingKey: "task.created", ct);
    }
}

/// <summary>
/// Handles TaskStatusChangedEvent: invalidates relevant caches and
/// broadcasts the board update to all workspace members.
/// </summary>
public sealed class TaskStatusChangedEventHandler(
    IMessageBus messageBus,
    ILogger<TaskStatusChangedEventHandler> logger)
    : INotificationHandler<TaskStatusChangedEvent>
{
    public async Task Handle(TaskStatusChangedEvent notification, CancellationToken ct)
    {
        logger.LogInformation(
            "Task {TaskId} status changed from {OldStatus} to {NewStatus}",
            notification.TaskId, notification.OldStatusId, notification.NewStatusId);

        await messageBus.PublishAsync(new
        {
            EventType    = "task.status_changed",
            notification.TaskId,
            notification.WorkspaceId,
            notification.ProjectId,
            notification.OldStatusId,
            notification.NewStatusId,
            notification.ChangedById,
            notification.OccurredAt,
        }, routingKey: "task.status_changed", ct);
    }
}
