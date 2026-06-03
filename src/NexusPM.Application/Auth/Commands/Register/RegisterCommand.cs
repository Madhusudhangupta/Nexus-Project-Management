using FluentValidation;
using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Aggregates.Users;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;
using NexusPM.Domain.ValueObjects;

namespace NexusPM.Application.Auth.Commands.Register;

// ── Register Command ──────────────────────────────────────────────────────────

public sealed record RegisterCommand : ICommand<RegisterResult>
{
    public required string Email { get; init; }
    public required string Password { get; init; }
    public required string DisplayName { get; init; }
    public required string WorkspaceName { get; init; }
}

public sealed record RegisterResult(
    Guid UserId,
    Guid WorkspaceId,
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public sealed class RegisterCommandHandler(
    IUnitOfWork uow,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtService)
    : IRequestHandler<RegisterCommand, RegisterResult>
{
    public async Task<RegisterResult> Handle(RegisterCommand cmd, CancellationToken ct)
    {
        var email = Email.Create(cmd.Email);

        if (await uow.Users.EmailExistsAsync(email.Normalized, ct))
            throw new DuplicateException("User", "email", cmd.Email);

        // Create user
        var passwordHash = passwordHasher.Hash(cmd.Password);
        var user = User.Create(email, cmd.DisplayName, passwordHash, createdById: Guid.Empty);

        uow.Users.Add(user);

        // Create first workspace for the user
        var workspace = Workspace.Create(cmd.WorkspaceName, user.Id);
        uow.Workspaces.Add(workspace);

        await uow.SaveChangesAsync(ct);

        // Issue tokens
        var claims = new TokenClaims
        {
            UserId      = user.Id,
            Email       = user.Email.Value,
            WorkspaceId = workspace.Id,
            Role        = "Admin",
            Permissions = GetAdminPermissions(),
        };

        var accessToken = jwtService.GenerateAccessToken(claims);
        var refreshPair = jwtService.GenerateRefreshToken();

        return new RegisterResult(
            user.Id, workspace.Id, accessToken, refreshPair.RawToken, ExpiresIn: 900);
    }

    private static IReadOnlyList<string> GetAdminPermissions() =>
    [
        "tasks:read", "tasks:write", "tasks:delete",
        "projects:read", "projects:write", "projects:delete",
        "workspaces:read", "workspaces:write", "members:manage",
    ];
}

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(320);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");

        RuleFor(x => x.DisplayName)
            .NotEmpty().WithMessage("Display name is required.")
            .MaximumLength(100);

        RuleFor(x => x.WorkspaceName)
            .NotEmpty().WithMessage("Workspace name is required.")
            .MaximumLength(100);
    }
}
