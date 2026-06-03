using NexusPM.Domain.Common;

namespace NexusPM.Domain.Aggregates.Users;

/// <summary>
/// Persistent refresh token. Stored as a SHA-256 hash — the raw token
/// is only ever held in memory during the request that issued it.
///
/// Rotation: each token is single-use. On refresh, the old token is
/// revoked and a new one is issued. Replaying a revoked token indicates
/// theft; all tokens for the user should be invalidated.
/// </summary>
public sealed class RefreshToken : Entity
{
    public Guid UserId { get; private init; }

    // Navigation property for EF Core — not part of domain logic
    public User User { get; private init; } = null!;

    public string TokenHash { get; private init; } = null!;
    public DateTimeOffset ExpiresAt { get; private init; }
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>The token that was replaced by this one (for audit trail).</summary>
    public Guid? ReplacedById { get; private init; }

    public string? IpAddress { get; private init; }
    public string? UserAgent { get; private init; }
    public DateTimeOffset CreatedAt { get; private init; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    private RefreshToken() { }

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        Guid? replacedById = null,
        string? ipAddress  = null,
        string? userAgent  = null)
    {
        return new RefreshToken
        {
            Id           = Guid.NewGuid(),
            UserId       = userId,
            TokenHash    = tokenHash,
            ExpiresAt    = expiresAt,
            ReplacedById = replacedById,
            IpAddress    = ipAddress,
            UserAgent    = userAgent,
            CreatedAt    = DateTimeOffset.UtcNow,
        };
    }

    public void Revoke(DateTimeOffset revokedAt) => RevokedAt = revokedAt;
}
