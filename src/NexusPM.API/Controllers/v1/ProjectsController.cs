using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusPM.Application.Projects.Commands.CreateProject;
using NexusPM.Application.Projects.Commands.DeleteProject;
using NexusPM.Application.Projects.Commands.UpdateProject;
using NexusPM.Application.Projects.Queries;

namespace NexusPM.API.Controllers.v1;

/// <summary>Project CRUD within a workspace.</summary>
[ApiController]
[Authorize]
[Route("api/v1/workspaces/{workspaceId:guid}/projects")]
public sealed class ProjectsController(IMediator mediator) : ControllerBase
{
    /// <summary>List all projects in a workspace.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProjectDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProjects(
        [FromRoute] Guid workspaceId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProjectsQuery(workspaceId), ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Get a project with its workflow states.</summary>
    [HttpGet("{projectId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProjectDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProject(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid projectId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProjectQuery(workspaceId, projectId), ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Create a new project in the workspace.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateProjectResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateProject(
        [FromRoute] Guid workspaceId,
        [FromBody] CreateProjectCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(command with { WorkspaceId = workspaceId }, ct);
        return CreatedAtAction(nameof(GetProject),
            new { workspaceId, projectId = result.ProjectId },
            ApiResponse.Ok(result));
    }

    /// <summary>Update project name and description.</summary>
    [HttpPut("{projectId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UpdateProjectResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProject(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid projectId,
        [FromBody] UpdateProjectCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            command with { WorkspaceId = workspaceId, ProjectId = projectId }, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Archive (soft-delete) a project.</summary>
    [HttpDelete("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteProject(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid projectId,
        CancellationToken ct)
    {
        await mediator.Send(new DeleteProjectCommand
        {
            WorkspaceId = workspaceId,
            ProjectId   = projectId,
        }, ct);
        return NoContent();
    }
}
