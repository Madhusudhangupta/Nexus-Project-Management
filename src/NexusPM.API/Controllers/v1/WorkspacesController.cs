using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexusPM.Application.Workspaces.Commands.CreateWorkspace;
using NexusPM.Application.Workspaces.Commands.InviteMember;
using NexusPM.Application.Workspaces.Commands.UpdateWorkspace;
using NexusPM.Application.Workspaces.Queries;

namespace NexusPM.API.Controllers.v1;

/// <summary>Workspace management — create, update, list members, invite.</summary>
[ApiController]
[Authorize]
[Route("api/v1/workspaces")]
public sealed class WorkspacesController(IMediator mediator) : ControllerBase
{
    /// <summary>List all workspaces the current user belongs to.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<WorkspaceDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWorkspaces(CancellationToken ct)
    {
        var result = await mediator.Send(new GetWorkspacesQuery(), ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Get workspace details by ID.</summary>
    [HttpGet("{workspaceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<WorkspaceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWorkspace(
        [FromRoute] Guid workspaceId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetWorkspaceQuery(workspaceId), ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Create a new workspace.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateWorkspaceResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateWorkspace(
        [FromBody] CreateWorkspaceCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetWorkspace),
            new { workspaceId = result.WorkspaceId },
            ApiResponse.Ok(result));
    }

    /// <summary>Update workspace name and logo.</summary>
    [HttpPut("{workspaceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UpdateWorkspaceResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateWorkspace(
        [FromRoute] Guid workspaceId,
        [FromBody] UpdateWorkspaceCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(command with { WorkspaceId = workspaceId }, ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>List all members of a workspace.</summary>
    [HttpGet("{workspaceId:guid}/members")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<WorkspaceMemberDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMembers(
        [FromRoute] Guid workspaceId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetMembersQuery(workspaceId), ct);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Invite a user to the workspace by email.</summary>
    [HttpPost("{workspaceId:guid}/members/invite")]
    [ProducesResponseType(typeof(ApiResponse<InviteMemberResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InviteMember(
        [FromRoute] Guid workspaceId,
        [FromBody] InviteMemberCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(command with { WorkspaceId = workspaceId }, ct);
        return Ok(ApiResponse.Ok(result));
    }
}
