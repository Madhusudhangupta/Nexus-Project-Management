using FluentValidation;
using MediatR;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;

namespace NexusPM.Application.Auth.Commands.RefreshToken
{

// ── Refresh Token Command ─────────────────────────────────────────────────────

public sealed record RefreshTokenCommand : ICommand<RefreshTokenResult>
{
    public required string RefreshToken { get; init; }
}

public sealed record RefreshTokenResult(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn);

public sealed class RefreshTokenCommandHandler(
    IUnitOfWork uow,
    IJwtTokenService jwtService,
    IDateTimeProvider clock)
    : IRequestHandler<RefreshTokenCommand, RefreshTokenResult>
{
    public async Task<RefreshTokenResult> Handle(RefreshTokenCommand cmd, CancellationToken ct)
    {
        var tokenHash = jwtService.ComputeHash(cmd.RefreshToken);

        var stored = await uow.Users.GetRefreshTokenAsync(tokenHash, ct);

        if (stored is null || stored.RevokedAt.HasValue || stored.ExpiresAt < clock.UtcNow)
            throw new UnauthorizedException("Refresh token is invalid or expired.");

        if (!stored.User.IsActive)
            throw new UnauthorizedException("User account is inactive.");

        // Revoke the used token (rotation — each token can only be used once)
        stored.Revoke(clock.UtcNow);

        // Get the workspace and role for the new token claims
        var membership = await uow.Workspaces.GetPrimaryMembershipAsync(stored.UserId, ct);

        if (membership is null)
            throw new UnauthorizedException("No workspace membership found.");

        var claims = new TokenClaims
        {
            UserId      = stored.UserId,
            Email       = stored.User.Email.Value,
            WorkspaceId = membership.WorkspaceId,
            Role        = membership.Role.ToString(),
            Permissions = GetPermissionsForRole(membership.Role),
        };

        var newAccess  = jwtService.GenerateAccessToken(claims);
        var newRefresh = jwtService.GenerateRefreshToken();

        // Issue new refresh token
        var newToken = Domain.Aggregates.Users.RefreshToken.Create(
            stored.UserId, newRefresh.TokenHash, newRefresh.ExpiresAt,
            replacedById: stored.Id);
        uow.Users.AddRefreshToken(newToken);

        await uow.SaveChangesAsync(ct);

        return new RefreshTokenResult(newAccess, newRefresh.RawToken, ExpiresIn: 900);
    }

    private static IReadOnlyList<string> GetPermissionsForRole(
        Domain.Aggregates.Workspaces.WorkspaceRole role) =>
        role switch
        {
            Domain.Aggregates.Workspaces.WorkspaceRole.Admin =>
            [
                "tasks:read", "tasks:write", "tasks:delete",
                "projects:read", "projects:write", "projects:delete",
                "workspaces:read", "workspaces:write", "members:manage",
                "sprints:manage", "audit:read"
            ],
            Domain.Aggregates.Workspaces.WorkspaceRole.Member =>
            [
                "tasks:read", "tasks:write",
                "projects:read", "workspaces:read"
            ],
            _ => ["tasks:read", "projects:read", "workspaces:read"]
        };
}

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required.");
    }
}

// ── Logout Command ────────────────────────────────────────────────────────────

}
namespace NexusPM.Application.Auth.Commands.Logout
{

public sealed record LogoutCommand : ICommand
{
    public required string RefreshToken { get; init; }
}

public sealed class LogoutCommandHandler(
    IUnitOfWork uow,
    IJwtTokenService jwtService,
    IDateTimeProvider clock)
    : IRequestHandler<LogoutCommand>
{
    public async Task Handle(LogoutCommand cmd, CancellationToken ct)
    {
        var tokenHash = jwtService.ComputeHash(cmd.RefreshToken);

        var stored = await uow.Users.GetRefreshTokenAsync(tokenHash, ct);

        // Silently succeed even if token not found — idempotent logout
        if (stored is not null && !stored.RevokedAt.HasValue)
            {
                stored.Revoke(clock.UtcNow);
                await uow.SaveChangesAsync(ct);
            }
        }
    }
}