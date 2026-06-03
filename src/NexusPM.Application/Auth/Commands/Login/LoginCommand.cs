using FluentValidation;
using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;

namespace NexusPM.Application.Auth.Commands.Login;

public sealed record LoginCommand : ICommand<LoginResult>
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required Guid WorkspaceId { get; init; }
}

public sealed record LoginResult(
    Guid UserId,
    Guid WorkspaceId,
    string DisplayName,
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public sealed class LoginCommandHandler(
    IUnitOfWork uow,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtService)
    : IRequestHandler<LoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(LoginCommand cmd, CancellationToken ct)
    {
        // Normalize email for lookup
        var normalizedEmail = cmd.Email.Trim().ToLowerInvariant();

        var user = await uow.Users.GetByEmailAsync(normalizedEmail, ct);

        // Deliberate: return same generic error for both "not found" and "wrong password"
        // to prevent user enumeration attacks.
        const string invalidMsg = "Invalid email or password.";

        if (user is null || !user.IsActive || user.PasswordHash is null)
            throw new UnauthorizedException(invalidMsg);

        if (!passwordHasher.Verify(cmd.Password, user.PasswordHash))
            throw new UnauthorizedException(invalidMsg);

        // Validate workspace membership
        var workspace = await uow.Workspaces.GetByIdWithMembersAsync(cmd.WorkspaceId, ct)
            ?? throw new NotFoundException("Workspace", cmd.WorkspaceId);

        var role = workspace.GetMemberRole(user.Id)
            ?? throw new UnauthorizedException("You are not a member of this workspace.");

        user.RecordLogin();
        await uow.SaveChangesAsync(ct);

        var permissions = GetPermissionsForRole(role);
        var claims = new TokenClaims
        {
            UserId      = user.Id,
            Email       = user.Email.Value,
            WorkspaceId = workspace.Id,
            Role        = role.ToString(),
            Permissions = permissions,
        };

        var accessToken  = jwtService.GenerateAccessToken(claims);
        var refreshPair  = jwtService.GenerateRefreshToken();

        return new LoginResult(
            user.Id, workspace.Id, user.DisplayName,
            accessToken, refreshPair.RawToken, ExpiresIn: 900);
    }

    private static IReadOnlyList<string> GetPermissionsForRole(WorkspaceRole role) =>
        role switch
        {
            WorkspaceRole.Admin =>
            [
                "tasks:read", "tasks:write", "tasks:delete",
                "projects:read", "projects:write", "projects:delete",
                "workspaces:read", "workspaces:write", "members:manage",
                "sprints:manage", "audit:read"
            ],
            WorkspaceRole.Member =>
            [
                "tasks:read", "tasks:write",
                "projects:read", "workspaces:read"
            ],
            WorkspaceRole.Viewer =>
            [
                "tasks:read", "projects:read", "workspaces:read"
            ],
            _ => []
        };
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();

        RuleFor(x => x.WorkspaceId)
            .NotEmpty().WithMessage("WorkspaceId is required.");
    }
}
