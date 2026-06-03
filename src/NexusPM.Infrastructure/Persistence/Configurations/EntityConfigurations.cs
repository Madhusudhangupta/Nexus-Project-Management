using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Aggregates.Tasks;
using NexusPM.Domain.Aggregates.Users;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.ValueObjects;

namespace NexusPM.Infrastructure.Persistence.Configurations;

// ── User ──────────────────────────────────────────────────────────────────────

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("user_id");

        builder.OwnsOne(u => u.Email, email =>
        {
            email.Property(e => e.Value).HasColumnName("email").HasMaxLength(320).IsRequired();
            email.Property(e => e.Normalized).HasColumnName("email_normalized").HasMaxLength(320).IsRequired();
            email.HasIndex(e => e.Normalized).IsUnique().HasFilter("deleted_at IS NULL");
        });

        builder.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
        builder.Property(u => u.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(2048);
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(128);
        builder.Property(u => u.EmailVerified).HasColumnName("email_verified").HasDefaultValue(false);
        builder.Property(u => u.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.CreatedById).HasColumnName("created_by_id");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.UpdatedById).HasColumnName("updated_by_id");
        builder.Property(u => u.DeletedAt).HasColumnName("deleted_at");

        builder.HasQueryFilter(u => u.DeletedAt == null);
    }
}

// ── Workspace ─────────────────────────────────────────────────────────────────

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("workspaces");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("workspace_id");

        builder.OwnsOne(w => w.Slug, slug =>
        {
            slug.Property(s => s.Value).HasColumnName("slug").HasMaxLength(63).IsRequired();
            slug.HasIndex(s => s.Value).IsUnique().HasFilter("deleted_at IS NULL");
        });

        builder.Property(w => w.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(w => w.LogoUrl).HasColumnName("logo_url").HasMaxLength(2048);
        builder.Property(w => w.Tier)
            .HasColumnName("tier")
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(SubscriptionTier.Free);
        builder.Property(w => w.OwnerId).HasColumnName("owner_id");
        builder.Property(w => w.CreatedAt).HasColumnName("created_at");
        builder.Property(w => w.CreatedById).HasColumnName("created_by_id");
        builder.Property(w => w.UpdatedAt).HasColumnName("updated_at");
        builder.Property(w => w.UpdatedById).HasColumnName("updated_by_id");
        builder.Property(w => w.DeletedAt).HasColumnName("deleted_at");

        builder.HasMany(w => w.Memberships)
            .WithOne()
            .HasForeignKey(m => m.WorkspaceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(w => w.DeletedAt == null);
    }
}

public sealed class WorkspaceMembershipConfiguration : IEntityTypeConfiguration<WorkspaceMembership>
{
    public void Configure(EntityTypeBuilder<WorkspaceMembership> builder)
    {
        builder.ToTable("workspace_memberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("membership_id");
        builder.Property(m => m.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(m => m.UserId).HasColumnName("user_id");
        builder.Property(m => m.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(m => m.InvitedById).HasColumnName("invited_by_id");
        builder.Property(m => m.JoinedAt).HasColumnName("joined_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(m => new { m.WorkspaceId, m.UserId }).IsUnique();
    }
}

// ── Project ───────────────────────────────────────────────────────────────────

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("project_id");
        builder.Property(p => p.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Key).HasColumnName("key").HasMaxLength(10).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description");
        builder.Property(p => p.IsArchived).HasColumnName("is_archived").HasDefaultValue(false);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.CreatedById).HasColumnName("created_by_id");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Property(p => p.UpdatedById).HasColumnName("updated_by_id");
        builder.Property(p => p.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(p => new { p.WorkspaceId, p.Key })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(p => p.WorkspaceId)
            .HasFilter("deleted_at IS NULL");

        builder.HasMany(p => p.WorkflowStates)
            .WithOne()
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Sprints)
            .WithOne()
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(p => p.DeletedAt == null);
    }
}

public sealed class WorkflowStateConfiguration : IEntityTypeConfiguration<WorkflowState>
{
    public void Configure(EntityTypeBuilder<WorkflowState> builder)
    {
        builder.ToTable("workflow_states");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("state_id");
        builder.Property(s => s.ProjectId).HasColumnName("project_id");
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(s => s.Color).HasColumnName("color").HasMaxLength(20).IsRequired();
        builder.Property(s => s.IsTerminal).HasColumnName("is_terminal").HasDefaultValue(false);
        builder.Property(s => s.Position).HasColumnName("position");
    }
}

public sealed class SprintConfiguration : IEntityTypeConfiguration<Sprint>
{
    public void Configure(EntityTypeBuilder<Sprint> builder)
    {
        builder.ToTable("sprints");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("sprint_id");
        builder.Property(s => s.ProjectId).HasColumnName("project_id");
        builder.Property(s => s.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(s => s.Goal).HasColumnName("goal");
        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(s => s.StartDate).HasColumnName("start_date");
        builder.Property(s => s.EndDate).HasColumnName("end_date");
        builder.Property(s => s.ClosedAt).HasColumnName("closed_at");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.CreatedById).HasColumnName("created_by_id");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");
        builder.Property(s => s.UpdatedById).HasColumnName("updated_by_id");

        builder.HasIndex(s => s.ProjectId);
        builder.HasIndex(s => s.WorkspaceId);
    }
}

// ── Task ──────────────────────────────────────────────────────────────────────

public sealed class PriorityConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Priority, int>
{
    public PriorityConverter() : base(p => p.Value, v => Priority.FromValue(v)) { }
}

public sealed class StoryPointsConverter : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Domain.ValueObjects.StoryPoints?, int?>
{
    public StoryPointsConverter() : base(
        sp => sp == null ? (int?)null : sp.Value,
        v => v == null ? null : Domain.ValueObjects.StoryPoints.Create(v.Value)) { }
}

public sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("task_id");
        builder.Property(t => t.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(t => t.ProjectId).HasColumnName("project_id");
        builder.Property(t => t.SprintId).HasColumnName("sprint_id");
        builder.Property(t => t.ParentTaskId).HasColumnName("parent_task_id");
        builder.Property(t => t.TaskKey).HasColumnName("task_key").HasMaxLength(20).IsRequired();
        builder.Property(t => t.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(t => t.Description).HasColumnName("description");
        builder.Property(t => t.StatusId).HasColumnName("status_id");
        builder.Property(t => t.AssigneeId).HasColumnName("assignee_id");
        builder.Property(t => t.ReporterId).HasColumnName("reporter_id");
        builder.Property(t => t.DueDate).HasColumnName("due_date");
        builder.Property(t => t.EstimatedHours).HasColumnName("estimated_hours").HasPrecision(6, 2);
        builder.Property(t => t.LoggedHours).HasColumnName("logged_hours").HasPrecision(6, 2).HasDefaultValue(0m);
        builder.Property(t => t.Position).HasColumnName("position").HasDefaultValue(0);
        builder.Property(t => t.NestingLevel).HasColumnName("nesting_level").HasDefaultValue(0);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        builder.Property(t => t.CreatedById).HasColumnName("created_by_id");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        builder.Property(t => t.UpdatedById).HasColumnName("updated_by_id");
        builder.Property(t => t.DeletedAt).HasColumnName("deleted_at");

        // Value object: Priority stored as integer
        builder.Property(t => t.Priority)
            .HasColumnName("priority")
            .HasConversion(new PriorityConverter());

        // Value object: StoryPoints stored as nullable integer
        builder.Property(t => t.StoryPoints)
            .HasColumnName("story_points")
            .HasConversion(new StoryPointsConverter());

        // Labels stored as PostgreSQL text array
        builder.Property(t => t.Labels)
            .HasColumnName("labels")
            .HasColumnType("text[]")
            .HasDefaultValueSql("'{}'");

        // Indexes
        builder.HasIndex(t => t.WorkspaceId).HasFilter("deleted_at IS NULL");
        builder.HasIndex(t => t.ProjectId).HasFilter("deleted_at IS NULL");
        builder.HasIndex(t => t.AssigneeId).HasFilter("deleted_at IS NULL");
        builder.HasIndex(t => t.SprintId).HasFilter("deleted_at IS NULL");
        builder.HasIndex(t => new { t.ProjectId, t.StatusId }).HasFilter("deleted_at IS NULL");

        // Owned collections
        builder.HasMany(t => t.Comments)
            .WithOne()
            .HasForeignKey(c => c.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Attachments)
            .WithOne()
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Dependencies)
            .WithOne()
            .HasForeignKey(d => d.SourceTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasQueryFilter(t => t.DeletedAt == null);
    }
}

public sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("task_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("comment_id");
        builder.Property(c => c.TaskId).HasColumnName("task_id");
        builder.Property(c => c.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(c => c.Content).HasColumnName("content").IsRequired();
        builder.Property(c => c.AuthorId).HasColumnName("author_id");
        builder.Property(c => c.IsEdited).HasColumnName("is_edited").HasDefaultValue(false);
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.Property(c => c.DeletedAt).HasColumnName("deleted_at");

        builder.HasIndex(c => c.TaskId);
        builder.HasQueryFilter(c => c.DeletedAt == null);
    }
}

public sealed class TaskAttachmentConfiguration : IEntityTypeConfiguration<TaskAttachment>
{
    public void Configure(EntityTypeBuilder<TaskAttachment> builder)
    {
        builder.ToTable("task_attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("attachment_id");
        builder.Property(a => a.TaskId).HasColumnName("task_id");
        builder.Property(a => a.WorkspaceId).HasColumnName("workspace_id");
        builder.Property(a => a.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(a => a.BlobUrl).HasColumnName("blob_url").HasMaxLength(2048).IsRequired();
        builder.Property(a => a.SizeBytes).HasColumnName("size_bytes");
        builder.Property(a => a.UploadedById).HasColumnName("uploaded_by_id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => a.TaskId);
    }
}

public sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("task_dependencies");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("dependency_id");
        builder.Property(d => d.SourceTaskId).HasColumnName("source_task_id");
        builder.Property(d => d.TargetTaskId).HasColumnName("target_task_id");
        builder.Property(d => d.Type)
            .HasColumnName("dependency_type")
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(d => new { d.SourceTaskId, d.TargetTaskId, d.Type }).IsUnique();
    }
}
