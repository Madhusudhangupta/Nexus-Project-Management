using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NexusPM.Application.Common.Interfaces;

namespace NexusPM.API.Hubs;

/// <summary>
/// SignalR hub for real-time task updates and board synchronization.
///
/// Group strategy:
///   workspace:{id}  — all members of a workspace receive board-level updates
///   task:{id}       — users viewing a specific task receive detail updates + presence
///
/// Scale-out: Redis backplane ensures messages reach clients on any API instance.
/// </summary>
[Authorize]
public sealed class TaskHub(ICurrentUser currentUser) : Hub
{
    /// <summary>Join the workspace group and register presence on connect.</summary>
    public override async Task OnConnectedAsync()
    {
        if (currentUser.IsAuthenticated)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                WorkspaceGroup(currentUser.WorkspaceId));
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Client automatically leaves all groups on disconnect
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Client calls this when opening a task detail view.
    /// Joins the task-level group and broadcasts the updated presence list.
    /// </summary>
    public async Task JoinTaskRoom(string taskId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, TaskGroup(taskId));

        // Notify others in the room that someone joined
        await Clients
            .OthersInGroup(TaskGroup(taskId))
            .SendAsync("UserJoinedTask", new
            {
                userId    = currentUser.UserId,
                taskId,
                joinedAt  = DateTimeOffset.UtcNow,
            });
    }

    /// <summary>Client calls this when closing a task detail view.</summary>
    public async Task LeaveTaskRoom(string taskId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, TaskGroup(taskId));
        await Clients
            .OthersInGroup(TaskGroup(taskId))
            .SendAsync("UserLeftTask", new
            {
                userId = currentUser.UserId,
                taskId,
            });
    }

    public static string WorkspaceGroup(Guid workspaceId) => $"workspace:{workspaceId}";
    public static string TaskGroup(string taskId)         => $"task:{taskId}";
    public static string TaskGroup(Guid taskId)           => $"task:{taskId}";
}

/// <summary>
/// Hub for in-app notifications. Each user is in their own personal group.
/// </summary>
[Authorize]
public sealed class NotificationHub(ICurrentUser currentUser) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (currentUser.IsAuthenticated)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                UserGroup(currentUser.UserId));
        }
        await base.OnConnectedAsync();
    }

    public static string UserGroup(Guid userId) => $"user:{userId}:notifications";
}

/// <summary>
/// Service for pushing events to SignalR groups from within the application layer.
/// Wraps IHubContext so non-hub code can send messages.
/// </summary>
public sealed class SignalRRealtimeService(
    IHubContext<TaskHub> taskHub,
    IHubContext<NotificationHub> notifHub) : IRealtimeService
{
    public async Task SendToUserAsync(
        Guid userId, string eventName, object payload, CancellationToken ct = default)
    {
        await notifHub.Clients
            .Group(NotificationHub.UserGroup(userId))
            .SendAsync(eventName, payload, ct);
    }

    public async Task SendToWorkspaceAsync(
        Guid workspaceId, string eventName, object payload, CancellationToken ct = default)
    {
        await taskHub.Clients
            .Group(TaskHub.WorkspaceGroup(workspaceId))
            .SendAsync(eventName, payload, ct);
    }

    public async Task SendToTaskRoomAsync(
        Guid taskId, string eventName, object payload, CancellationToken ct = default)
    {
        await taskHub.Clients
            .Group(TaskHub.TaskGroup(taskId))
            .SendAsync(eventName, payload, ct);
    }
}
