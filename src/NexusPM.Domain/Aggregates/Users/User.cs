using NexusPM.Domain.Common;
using NexusPM.Domain.Events.Workspaces;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.ValueObjects;

namespace NexusPM.Domain.Aggregates.Users;

/// <summary>
/// User aggregate root. Represents a platform user account.
/// Password hashing is performed by the infrastructure layer (bcrypt);
/// the domain only validates that a hash is present.
/// </summary>
public sealed class User : AggregateRoot, IAuditableEntity, ISoftDeletable
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public Email Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? AvatarUrl { get; private set; }

    // ── Credentials ───────────────────────────────────────────────────────────
    /// <summary>bcrypt hash. Null for OAuth-only users.</summary>
    public string? PasswordHash { get; private set; }
    public bool EmailVerified { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset? LastLoginAt { get; private set; }

    // ── Audit ─────────────────────────────────────────────────────────────────
    public DateTimeOffset CreatedAt { get; private init; }
    public Guid CreatedById { get; private init; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid? UpdatedById { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    // EF Core constructor
    private User() { }

    /// <summary>Creates a new user with an email/password credential.</summary>
    public static User Create(Email email, string displayName, string passwordHash, Guid createdById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id          = Guid.NewGuid(),
            Email       = email,
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            CreatedAt   = now,
            CreatedById = createdById,
            UpdatedAt   = now,
        };

        return user;
    }

    /// <summary>Creates a user via OAuth (no password hash).</summary>
    public static User CreateFromOAuth(Email email, string displayName, string? avatarUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var now = DateTimeOffset.UtcNow;
        return new User
        {
            Id          = Guid.NewGuid(),
            Email       = email,
            DisplayName = displayName.Trim(),
            AvatarUrl   = avatarUrl,
            EmailVerified = true, // OAuth provider validates email
            CreatedAt   = now,
            CreatedById = Guid.Empty, // system-created
            UpdatedAt   = now,
        };
    }

    /// <summary>Updates the password hash after validation in the application layer.</summary>
    public void ChangePassword(string newPasswordHash, Guid updatedById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;

        RaiseDomainEvent(new UserPasswordChangedEvent(Id));
    }

    public void MarkEmailVerified()
    {
        EmailVerified = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordLogin()
    {
        LastLoginAt = DateTimeOffset.UtcNow;
        UpdatedAt = LastLoginAt.Value;
    }

    public void UpdateProfile(string? displayName, string? avatarUrl, Guid updatedById)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            DisplayName = displayName.Trim();
        AvatarUrl = avatarUrl;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
    }

    public void Deactivate(Guid updatedById)
    {
        if (!IsActive)
            throw new BusinessRuleViolationException("UserDeactivation", "User is already inactive.");
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        UpdatedById = updatedById;
    }

    public void SoftDelete(Guid deletedById)
    {
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DeletedAt.Value;
        UpdatedById = deletedById;
    }
}
