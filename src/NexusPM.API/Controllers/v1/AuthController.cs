using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexusPM.Application.Auth.Commands.Login;
using NexusPM.Application.Auth.Commands.Register;

namespace NexusPM.API.Controllers.v1;

/// <summary>Authentication endpoints — register, login, token refresh, logout.</summary>
[ApiController]
[Route("api/v1/auth")]
[AllowAnonymous]
public sealed class AuthController(IMediator mediator) : ControllerBase
{
    /// <summary>Register a new user and create their first workspace.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<RegisterResult>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return Created(string.Empty, ApiResponse.Ok(result));
    }

    /// <summary>Authenticate and receive JWT access + refresh tokens.</summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<LoginResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginCommand command,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return Ok(ApiResponse.Ok(result));
    }
}
