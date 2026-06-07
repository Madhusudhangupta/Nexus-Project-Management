using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Aggregates.Users;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Repositories;
using NexusPM.Domain.ValueObjects;
using NexusPM.Infrastructure.Persistence;
using NexusPM.Infrastructure.Persistence.Repositories;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace NexusPM.Infrastructure.Tests.Persistence.Repositories;

// ── Database Fixture (shared across all tests in collection) ──────────────────

public sealed class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private Respawner? _respawner;

    private readonly bool _isCi = Environment.GetEnvironmentVariable("CI") == "true";

    public AppDbContext DbContext { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!_isCi)
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("nexuspm_test")
                .WithUsername("nexuspm")
                .WithPassword("test_password")
                .Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        else
        {
            ConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection") 
                ?? "Host=127.0.0.1;Port=5432;Database=nexuspm_test;Username=nexuspm;Password=test_password";
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        DbContext = new AppDbContext(options, new TestCurrentTenant(), new TestPublisher());

        // Apply all migrations
        await DbContext.Database.MigrateAsync();

        var connection = DbContext.Database.GetDbConnection();
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
        });
    }

    /// <summary>Resets the database to a clean state between tests.</summary>
    public async Task ResetAsync()
    {
        var connection = DbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
            
        await _respawner!.ResetAsync(connection);
    }

    public async Task DisposeAsync()
    {
        await DbContext.DisposeAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    public async Task<User> CreateUserAsync(string email = "test@example.com")
    {
        var user = User.Create(Email.Create(email), "Test User", "$2a$12$fakeHash", Guid.Empty);
        DbContext.Users.Add(user);
        await DbContext.SaveChangesAsync();
        return user;
    }

    public async Task<Workspace> CreateWorkspaceAsync(Guid ownerId)
    {
        var workspace = Workspace.Create("Test Workspace", ownerId);
        DbContext.Workspaces.Add(workspace);
        await DbContext.SaveChangesAsync();
        return workspace;
    }

    public async Task<Project> CreateProjectAsync(Guid workspaceId, Guid creatorId)
    {
        var project = Project.Create(workspaceId, "Test Project", "TEST", null, creatorId);
        DbContext.Projects.Add(project);
        await DbContext.SaveChangesAsync();
        return project;
    }
}

[CollectionDefinition("Database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

// ── Repository Tests ──────────────────────────────────────────────────────────

[Collection("Database")]
public sealed class TaskRepositoryTests(DatabaseFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => db.ResetAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    [Fact]
    public async Task Add_AndGetById_ShouldPersistAndRetrieveTask()
    {
        // Arrange
        var user      = await db.CreateUserAsync();
        var workspace = await db.CreateWorkspaceAsync(user.Id);
        var project   = await db.CreateProjectAsync(workspace.Id, user.Id);
        var stateId   = project.WorkflowStates.First().Id;

        var task = Domain.Aggregates.Tasks.TaskItem.Create(
            workspace.Id, project.Id, "TEST-1", "Integration test task", stateId, user.Id);

        var repo = new TaskRepository(db.DbContext);

        // Act
        repo.Add(task);
        await db.DbContext.SaveChangesAsync();

        var retrieved = await repo.GetByIdAsync(task.Id);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be("Integration test task");
        retrieved.TaskKey.Should().Be("TEST-1");
        retrieved.ProjectId.Should().Be(project.Id);
    }

    [Fact]
    public async Task GetProjectTasksAsync_WithPriorityFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        var user      = await db.CreateUserAsync("filter@example.com");
        var workspace = await db.CreateWorkspaceAsync(user.Id);
        var project   = await db.CreateProjectAsync(workspace.Id, user.Id);
        var stateId   = project.WorkflowStates.First().Id;

        var repo = new TaskRepository(db.DbContext);

        // Seed 5 high + 5 low priority tasks
        for (var i = 1; i <= 5; i++)
        {
            var high = Domain.Aggregates.Tasks.TaskItem.Create(
                workspace.Id, project.Id, $"TEST-{i}", $"High task {i}", stateId, user.Id,
                Domain.ValueObjects.Priority.High);
            repo.Add(high);
        }
        for (var i = 6; i <= 10; i++)
        {
            var low = Domain.Aggregates.Tasks.TaskItem.Create(
                workspace.Id, project.Id, $"TEST-{i}", $"Low task {i}", stateId, user.Id,
                Domain.ValueObjects.Priority.Low);
            repo.Add(low);
        }
        await db.DbContext.SaveChangesAsync();

        // Act
        var filter     = new TaskQueryFilter { Priority = 2 }; // High
        var pagination = new PaginationParams { Page = 1, PageSize = 25 };
        var result     = await repo.GetProjectTasksAsync(project.Id, filter, pagination);

        // Assert
        result.TotalCount.Should().Be(5);
        result.Items.Should().AllSatisfy(t =>
            t.Priority.Should().Be(Domain.ValueObjects.Priority.High));
    }

    [Fact]
    public async Task SoftDelete_ShouldExcludeFromNormalQueries()
    {
        // Arrange
        var user      = await db.CreateUserAsync("delete@example.com");
        var workspace = await db.CreateWorkspaceAsync(user.Id);
        var project   = await db.CreateProjectAsync(workspace.Id, user.Id);
        var stateId   = project.WorkflowStates.First().Id;

        var task = Domain.Aggregates.Tasks.TaskItem.Create(
            workspace.Id, project.Id, "TEST-DEL", "To be deleted", stateId, user.Id);

        var repo = new TaskRepository(db.DbContext);
        repo.Add(task);
        await db.DbContext.SaveChangesAsync();

        // Act: soft delete
        task.SoftDelete(user.Id);
        repo.Update(task);
        await db.DbContext.SaveChangesAsync();

        // Assert: not found in normal query (global query filter applied)
        var retrieved = await repo.GetByIdAsync(task.Id);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectTasksAsync_Pagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var user      = await db.CreateUserAsync("page@example.com");
        var workspace = await db.CreateWorkspaceAsync(user.Id);
        var project   = await db.CreateProjectAsync(workspace.Id, user.Id);
        var stateId   = project.WorkflowStates.First().Id;

        var repo = new TaskRepository(db.DbContext);

        for (var i = 1; i <= 15; i++)
        {
            var t = Domain.Aggregates.Tasks.TaskItem.Create(
                workspace.Id, project.Id, $"TEST-{i}", $"Task {i}", stateId, user.Id);
            repo.Add(t);
        }
        await db.DbContext.SaveChangesAsync();

        // Act: page 2, 5 items per page
        var filter     = new TaskQueryFilter();
        var pagination = new PaginationParams { Page = 2, PageSize = 5 };
        var result     = await repo.GetProjectTasksAsync(project.Id, filter, pagination);

        // Assert
        result.TotalCount.Should().Be(15);
        result.Items.Count.Should().Be(5);
        result.Page.Should().Be(2);
        result.TotalPages.Should().Be(3);
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }
}

[Collection("Database")]
public sealed class WorkspaceRepositoryTests(DatabaseFixture db) : IAsyncLifetime
{
    public Task InitializeAsync() => db.ResetAsync();
    public Task DisposeAsync()    => Task.CompletedTask;

    [Fact]
    public async Task GetBySlug_ExistingSlug_ShouldReturnWorkspace()
    {
        var user      = await db.CreateUserAsync("slug@example.com");
        var workspace = await db.CreateWorkspaceAsync(user.Id);
        var repo      = new WorkspaceRepository(db.DbContext);

        var result = await repo.GetBySlugAsync(workspace.Slug.Value);

        result.Should().NotBeNull();
        result!.Id.Should().Be(workspace.Id);
    }

    [Fact]
    public async Task InviteMember_ShouldPersistMembership()
    {
        var owner   = await db.CreateUserAsync("owner2@example.com");
        var invitee = await db.CreateUserAsync("invitee@example.com");
        var workspace = Workspace.Create("Invite Test", owner.Id);
        db.DbContext.Workspaces.Add(workspace);
        await db.DbContext.SaveChangesAsync();

        // Act: invite via domain method
        workspace.InviteMember(invitee.Id, WorkspaceRole.Member, owner.Id);
        db.DbContext.Workspaces.Update(workspace);
        await db.DbContext.SaveChangesAsync();

        // Assert: membership persisted
        var loaded = await new WorkspaceRepository(db.DbContext)
            .GetByIdWithMembersAsync(workspace.Id);

        loaded!.Memberships.Should().HaveCount(2); // owner + invitee
        loaded.Memberships.Should().Contain(m =>
            m.UserId == invitee.Id && m.Role == WorkspaceRole.Member);
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

internal sealed class TestCurrentTenant : ICurrentTenant
{
    public Guid WorkspaceId { get; private set; } = Guid.Empty;
    public void SetTenant(Guid workspaceId) => WorkspaceId = workspaceId;
}

internal sealed class TestPublisher : MediatR.IPublisher
{
    public Task Publish(object notification, CancellationToken ct = default) =>
        Task.CompletedTask;
    public Task Publish<TNotification>(TNotification notification, CancellationToken ct = default)
        where TNotification : MediatR.INotification =>
        Task.CompletedTask;
}
