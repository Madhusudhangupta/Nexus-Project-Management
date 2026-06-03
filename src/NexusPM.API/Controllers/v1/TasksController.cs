using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusPM.Application.Tasks.Commands.AssignTask;
using NexusPM.Application.Tasks.Commands.ChangeTaskStatus;
using NexusPM.Application.Tasks.Commands.CreateTask;
using NexusPM.Application.Tasks.Queries.GetTask;
using NexusPM.Application.Common.Interfaces;

namespace NexusPM.API.Controllers.v1;

/// <summary>Task CRUD and lifecycle operations.</summary>
[ApiController]
[Authorize]
[Route("api/v1/workspaces/{workspaceId:guid}/projects/{projectId:guid}/tasks")]
public sealed class TasksController(IMediator mediator) : ControllerBase
{
    /// <summary>List tasks in a project with filtering, sorting, and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedApiResponse<TaskSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTasks(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid projectId,
        [FromQuery] GetProjectTasksQuery query,
        CancellationToken ct)
    {
        var result = await mediator.Send(query with
        {
            WorkspaceId = workspaceId,
            ProjectId   = projectId,
        }, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Create a new task in the project.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateTaskResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateTask(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid projectId,
        [FromBody] CreateTaskCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(command with
        {
            WorkspaceId = workspaceId,
            ProjectId   = projectId,
        }, ct);

        return Created($"/api/v1/workspaces/{workspaceId}/tasks/{result.TaskId}", ApiResponse.Ok(result));
    }
}

/// <summary>Single task operations (detail, update, delete).</summary>
[ApiController]
[Authorize]
[Route("api/v1/workspaces/{workspaceId:guid}/tasks")]
public sealed class TaskDetailController(IMediator mediator) : ControllerBase
{
    /// <summary>Get detailed view of a task including comments and attachments.</summary>
    [HttpGet("{taskId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<TaskDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTask(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid taskId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetTaskQuery(workspaceId, taskId), ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Change a task's workflow status.</summary>
    [HttpPatch("{taskId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<ChangeTaskStatusResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangeStatus(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid taskId,
        [FromBody] ChangeTaskStatusRequest request,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ChangeTaskStatusCommand
        {
            WorkspaceId = workspaceId,
            TaskId      = taskId,
            NewStatusId = request.StatusId,
        }, ct);
        return Ok(ApiResponse.Ok(result));
    }
}

public sealed record ChangeTaskStatusRequest(Guid StatusId);
