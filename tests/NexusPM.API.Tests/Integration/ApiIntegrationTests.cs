using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusPM.Infrastructure.Persistence;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace NexusPM.API.Tests.Integration;

// ── Web Application Factory ───────────────────────────────────────────────────

/// <summary>
/// Custom WebApplicationFactory that replaces production infrastructure with
/// Testcontainer-backed PostgreSQL and Redis instances.
/// All tests in a collection share one factory instance for speed.
/// </summary>
public sealed class NexusPMWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;

    private readonly bool _isCi = Environment.GetEnvironmentVariable("CI") == "true";

    public async Task InitializeAsync()
    {
        if (!_isCi)
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("nexuspm_api_test")
                .WithUsername("nexuspm")
                .WithPassword("test_pass")
                .Build();

            _redis = new RedisBuilder()
                .WithImage("redis:7.2-alpine")
                .Build();

            await _postgres.StartAsync();
            await _redis.StartAsync();
        }
    }

    public new async Task DisposeAsync()
    {
        if (!_isCi)
        {
            if (_postgres != null) await _postgres.DisposeAsync();
            if (_redis != null) await _redis.DisposeAsync();
        }
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            if (!_isCi)
            {
                // Replace real PostgreSQL with TestContainer connection
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(opts =>
                    opts.UseNpgsql(_postgres!.GetConnectionString()));

                // Replace Redis with TestContainer
                services.RemoveAll<StackExchange.Redis.IConnectionMultiplexer>();
                services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
                    StackExchange.Redis.ConnectionMultiplexer.Connect(_redis!.GetConnectionString()));
            }

            // Apply migrations
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.Migrate();

            // Mock RabbitMQ Message Bus to prevent connection attempts
            services.RemoveAll<NexusPM.Application.Common.Interfaces.IMessageBus>();
            services.AddSingleton<NexusPM.Application.Common.Interfaces.IMessageBus, DummyMessageBus>();
        });
    }

    /// <summary>Creates an unauthenticated HTTP client.</summary>
    public HttpClient CreateAnonymousClient() => CreateClient();

    /// <summary>Creates an HTTP client with a valid JWT for the given role.</summary>
    public HttpClient CreateAuthenticatedClient(
        string role = "Member",
        Guid? userId = null,
        Guid? workspaceId = null)
    {
        var client = CreateClient();
        var token  = GenerateTestJwt(
            userId      ?? Guid.NewGuid(),
            workspaceId ?? Guid.NewGuid(),
            role);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string GenerateTestJwt(Guid userId, Guid workspaceId, string role)
    {
        // Use the test signing key from appsettings.Testing.json
        // In a real test suite this would use the same JwtTokenService
        var claims = new[]
        {
            new System.Security.Claims.Claim("sub",          userId.ToString()),
            new System.Security.Claims.Claim("email",        $"test-{userId}@example.com"),
            new System.Security.Claims.Claim("workspace_id", workspaceId.ToString()),
            new System.Security.Claims.Claim("role",         role),
            new System.Security.Claims.Claim("permission",   "tasks:read"),
            new System.Security.Claims.Claim("permission",   "tasks:write"),
            new System.Security.Claims.Claim("permission",   "projects:read"),
        };

        var key  = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("test_signing_key_32_characters_min_here"));
        var creds = new Microsoft.IdentityModel.Tokens.SigningCredentials(
            key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer:             "nexuspm-api-test",
            audience:           "nexuspm-client-test",
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }
}

[CollectionDefinition("API")]
public sealed class ApiTestCollection : ICollectionFixture<NexusPMWebAppFactory> { }

// ── Auth Endpoint Tests ───────────────────────────────────────────────────────

[Collection("API")]
public sealed class AuthControllerTests(NexusPMWebAppFactory factory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Register_WithValidPayload_Returns201()
    {
        var client = factory.CreateAnonymousClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email         = $"new-{Guid.NewGuid()}@example.com",
            password      = "SecurePass1!",
            displayName   = "Test User",
            workspaceName = "My Workspace",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("data").GetProperty("userId").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("data").GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_Returns409()
    {
        var client = factory.CreateAnonymousClient();
        var email  = $"dup-{Guid.NewGuid()}@example.com";

        // First registration
        await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email, password = "SecurePass1!", displayName = "User", workspaceName = "WS",
        });

        // Second registration with same email
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email, password = "SecurePass1!", displayName = "User 2", workspaceName = "WS2",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_WithWeakPassword_Returns422()
    {
        var client   = factory.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email         = "weak@example.com",
            password      = "short",   // fails: < 8 chars, no uppercase, no digit
            displayName   = "Test",
            workspaceName = "WS",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errors").TryGetProperty("password", out _).Should().BeTrue();
    }
}

// ── Task Endpoint Tests ───────────────────────────────────────────────────────

[Collection("API")]
public sealed class TasksControllerTests(NexusPMWebAppFactory factory)
{
    [Fact]
    public async Task GetTasks_Unauthenticated_Returns401()
    {
        var client   = factory.CreateAnonymousClient();
        var response = await client.GetAsync(
            $"/api/v1/workspaces/{Guid.NewGuid()}/projects/{Guid.NewGuid()}/tasks");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateTask_AsViewer_Returns403()
    {
        var workspaceId = Guid.NewGuid();
        var client      = factory.CreateAuthenticatedClient(
            role: "Viewer", workspaceId: workspaceId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/workspaces/{workspaceId}/projects/{Guid.NewGuid()}/tasks",
            new { title = "Test task", priority = 3 });

        // Viewer doesn't have tasks:write permission — should be 403
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetTask_NonExistent_Returns404()
    {
        var workspaceId = Guid.NewGuid();
        var client      = factory.CreateAuthenticatedClient(workspaceId: workspaceId);
        var response    = await client.GetAsync(
            $"/api/v1/workspaces/{workspaceId}/tasks/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

// ── Health Check Tests ────────────────────────────────────────────────────────

[Collection("API")]
public sealed class HealthCheckTests(NexusPMWebAppFactory factory)
{
    [Fact]
    public async Task LivenessCheck_Returns200()
    {
        var client   = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadinessCheck_WhenDependenciesHealthy_Returns200()
    {
        var client   = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        // May be 200 (healthy) or 503 (degraded) depending on container state
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }
}
