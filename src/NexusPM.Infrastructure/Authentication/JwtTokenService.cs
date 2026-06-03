using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NexusPM.Application.Common.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace NexusPM.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public required string SigningKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int AccessTokenExpiryMinutes { get; init; } = 15;
    public int RefreshTokenExpiryDays { get; init; } = 7;
}

/// <summary>
/// Generates and validates JWT access tokens.
/// Tokens are signed with HMAC-SHA256 using a key from Azure Key Vault.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JwtSecurityTokenHandler _handler = new();

    public string GenerateAccessToken(TokenClaims claims)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.SigningKey));

        var signingCredentials = new SigningCredentials(
            key, SecurityAlgorithms.HmacSha256);

        var claimsList = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   claims.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, claims.Email),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new("workspace_id", claims.WorkspaceId.ToString()),
            new("role",         claims.Role),
        };

        // Add each permission as a separate claim
        claimsList.AddRange(
            claims.Permissions.Select(p => new Claim("permission", p)));

        var token = new JwtSecurityToken(
            issuer:             _options.Issuer,
            audience:           _options.Audience,
            claims:             claimsList,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddMinutes(_options.AccessTokenExpiryMinutes),
            signingCredentials: signingCredentials);

        return _handler.WriteToken(token);
    }

    public RefreshTokenPair GenerateRefreshToken()
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var tokenHash = ComputeHash(rawToken);
        return new RefreshTokenPair
        {
            RawToken  = rawToken,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenExpiryDays),
        };
    }

    public TokenClaims? ValidateAccessToken(string token)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.SigningKey));

        try
        {
            _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey        = key,
                ValidateIssuer          = true,
                ValidIssuer             = _options.Issuer,
                ValidateAudience        = true,
                ValidAudience           = _options.Audience,
                ValidateLifetime        = true,
                ClockSkew               = TimeSpan.Zero,
            }, out var validatedToken);

            var jwt = (JwtSecurityToken)validatedToken;
            return new TokenClaims
            {
                UserId      = Guid.Parse(jwt.Subject),
                Email       = jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Email).Value,
                WorkspaceId = Guid.Parse(jwt.Claims.First(c => c.Type == "workspace_id").Value),
                Role        = jwt.Claims.First(c => c.Type == "role").Value,
                Permissions = jwt.Claims
                    .Where(c => c.Type == "permission")
                    .Select(c => c.Value)
                    .ToList(),
            };
        }
        catch
        {
            return null;
        }
    }

    public string ComputeHash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>BCrypt password hasher. Cost factor 12 gives ~250ms on modern hardware.</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plainText) =>
        BCrypt.Net.BCrypt.HashPassword(plainText, WorkFactor);

    public bool Verify(string plainText, string hash) =>
        BCrypt.Net.BCrypt.Verify(plainText, hash);
}
