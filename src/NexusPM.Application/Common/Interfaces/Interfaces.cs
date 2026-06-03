namespace NexusPM.Application.Common.Interfaces;

/// <summary>Provides the identity of the currently authenticated user.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
    Guid WorkspaceId { get; }
    string Email { get; }
    string Role { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    bool HasPermission(string permission);
}

/// <summary>Provides the current tenant context (resolved from JWT).</summary>
public interface ICurrentTenant
{
    Guid WorkspaceId { get; }
    void SetTenant(Guid workspaceId);
}

/// <summary>Provides an accurate UTC timestamp. Abstracted for testability.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
    DateOnly TodayUtc { get; }
}

/// <summary>Distributed cache abstraction backed by Redis.</summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
}

/// <summary>
/// Outbound message bus for publishing integration events to RabbitMQ.
/// </summary>
public interface IMessageBus
{
    Task PublishAsync<T>(T message, string? routingKey = null, CancellationToken ct = default)
        where T : class;
}

/// <summary>Email delivery service. Backed by SendGrid in production.</summary>
public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class EmailMessage
{
    public required string To { get; init; }
    public required string ToName { get; init; }
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? PlainTextBody { get; init; }
    public string? TemplateId { get; init; }
    public Dictionary<string, string> TemplateData { get; init; } = [];
}

/// <summary>File/blob storage abstraction backed by Azure Blob Storage.</summary>
public interface IFileStorageService
{
    Task<string> UploadAsync(
        Stream fileStream, string containerName, string blobName,
        string contentType, CancellationToken ct = default);

    Task<string> GetPresignedUrlAsync(
        string containerName, string blobName,
        TimeSpan expiry, CancellationToken ct = default);

    Task DeleteAsync(string containerName, string blobName, CancellationToken ct = default);
}

/// <summary>Real-time notification hub abstraction backed by SignalR.</summary>
public interface IRealtimeService
{
    Task SendToUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);
    Task SendToWorkspaceAsync(Guid workspaceId, string eventName, object payload, CancellationToken ct = default);
    Task SendToTaskRoomAsync(Guid taskId, string eventName, object payload, CancellationToken ct = default);
}

/// <summary>Password hashing and verification. Backed by BCrypt.</summary>
public interface IPasswordHasher
{
    string Hash(string plainText);
    bool Verify(string plainText, string hash);
}

/// <summary>JWT token generation and validation.</summary>
public interface IJwtTokenService
{
    string GenerateAccessToken(TokenClaims claims);
    RefreshTokenPair GenerateRefreshToken();
    TokenClaims? ValidateAccessToken(string token);
    string ComputeHash(string rawToken);
}

public sealed class TokenClaims
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required Guid WorkspaceId { get; init; }
    public required string Role { get; init; }
    public required IReadOnlyList<string> Permissions { get; init; }
}

public sealed class RefreshTokenPair
{
    public required string RawToken { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
