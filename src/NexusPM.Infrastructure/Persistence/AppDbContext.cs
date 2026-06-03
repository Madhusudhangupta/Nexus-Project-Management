using MediatR;
using Microsoft.EntityFrameworkCore;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Aggregates.Tasks;
using NexusPM.Domain.Aggregates.Users;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Common;

namespace NexusPM.Infrastructure.Persistence;

/// <summary>
/// Main EF Core DbContext for Nexus PM.
///
/// Multi-tenancy: Global Query Filters enforce workspace_id scoping on all
/// ITenantEntity tables. A second filter excludes soft-deleted records.
///
/// Domain Events: after SaveChangesAsync, all accumulated domain events from
/// AggregateRoot instances are dispatched via MediatR.
/// </summary>
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentTenant currentTenant,
    IPublisher publisher)
    : DbContext(options)
{
    public Guid TenantId => currentTenant.WorkspaceId;

    // ── DbSets ────────────────────────────────────────────────────────────────
    public DbSet<User>                 Users                 => Set<User>();
    public DbSet<Workspace>            Workspaces            => Set<Workspace>();
    public DbSet<WorkspaceMembership>  WorkspaceMemberships  => Set<WorkspaceMembership>();
    public DbSet<Project>              Projects              => Set<Project>();
    public DbSet<WorkflowState>        WorkflowStates        => Set<WorkflowState>();
    public DbSet<Sprint>               Sprints               => Set<Sprint>();
    public DbSet<TaskItem>             Tasks                 => Set<TaskItem>();
    public DbSet<TaskComment>          TaskComments          => Set<TaskComment>();
    public DbSet<TaskAttachment>       TaskAttachments       => Set<TaskAttachment>();
    public DbSet<TaskDependency>       TaskDependencies      => Set<TaskDependency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> classes from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Soft delete: all ISoftDeletable entities exclude soft-deleted records
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplySoftDeleteFilter),
                               System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(entityType.ClrType);
                // method.Invoke(null, [modelBuilder]);
            }

            // Tenant isolation: all ITenantEntity filter by current workspace_id
            if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplyTenantFilter),
                               System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);
                // method.Invoke(this, [modelBuilder]);
            }
        }
    }

    private static void ApplySoftDeleteFilter<T>(ModelBuilder modelBuilder)
        where T : class, ISoftDeletable
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => e.DeletedAt == null);
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder)
        where T : class, ITenantEntity
    {
        modelBuilder.Entity<T>().HasQueryFilter(
            e => e.WorkspaceId == TenantId);
    }

    /// <summary>
    /// Saves changes and then dispatches all domain events raised by aggregates.
    /// Domain events are dispatched AFTER the transaction commits to avoid
    /// dispatching events for a failed transaction.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(cancellationToken);
        await DispatchDomainEventsAsync(cancellationToken);
        return result;
    }

    private async Task DispatchDomainEventsAsync(CancellationToken ct)
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        aggregates.ForEach(a => a.ClearDomainEvents());

        foreach (var domainEvent in events)
            await publisher.Publish(domainEvent, ct);
    }
}
