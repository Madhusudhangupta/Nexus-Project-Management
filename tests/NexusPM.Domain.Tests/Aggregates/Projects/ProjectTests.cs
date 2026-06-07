using FluentAssertions;
using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Events.Projects;
using NexusPM.Domain.Exceptions;
using Xunit;

namespace NexusPM.Domain.Tests.Aggregates.Projects;

public sealed class ProjectTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid UserId      = Guid.NewGuid();

    private static Project CreateProject(string name = "Test Project", string key = "TEST") =>
        Project.Create(WorkspaceId, name, key, null, UserId);

    // ── Creation ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithValidInput_ShouldSeedDefaultWorkflowStates()
    {
        var project = CreateProject();

        project.WorkflowStates.Should().HaveCount(4);
        project.WorkflowStates.Should().Contain(s => s.Name == "Backlog");
        project.WorkflowStates.Should().Contain(s => s.Name == "In Progress");
        project.WorkflowStates.Should().Contain(s => s.Name == "In Review");
        project.WorkflowStates.Should().Contain(s => s.Name == "Done");
    }

    [Fact]
    public void Create_ShouldRaiseProjectCreatedEvent()
    {
        var project = CreateProject();

        project.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ProjectCreatedEvent>();
    }

    [Fact]
    public void Create_KeyShouldBeUppercase()
    {
        var project = Project.Create(WorkspaceId, "Test", "mykey", null, UserId);
        project.Key.Should().Be("MYKEY");
    }

    [Theory]
    [InlineData("A")]           // too short (1 char)
    [InlineData("TOOLONGKEY1")] // too long (11 chars)
    [InlineData("KEY-1")]       // invalid char
    public void Create_InvalidKey_ShouldThrowDomainException(string key)
    {
        var act = () => Project.Create(WorkspaceId, "Test", key, null, UserId);
        act.Should().Throw<DomainException>();
    }

    // ── Workflow States ───────────────────────────────────────────────────────

    [Fact]
    public void AddWorkflowState_NewState_ShouldAddSuccessfully()
    {
        var project = CreateProject();
        var state   = project.AddWorkflowState("Blocked", "#EF4444", false, 2, UserId);

        project.WorkflowStates.Should().Contain(s => s.Name == "Blocked");
        state.Color.Should().Be("#EF4444");
    }

    [Fact]
    public void AddWorkflowState_DuplicateName_ShouldThrowDuplicateException()
    {
        var project = CreateProject();

        var act = () => project.AddWorkflowState("Backlog", "#000000", false, 10, UserId);
        act.Should().Throw<DuplicateException>();
    }

    // ── Sprints ───────────────────────────────────────────────────────────────

    [Fact]
    public void CreateSprint_ValidDates_ShouldAddSprint()
    {
        var project = CreateProject();
        var sprint  = project.CreateSprint(
            "Sprint 1", "Deliver login feature",
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            UserId);

        project.Sprints.Should().ContainSingle();
        sprint.Status.Should().Be(SprintStatus.Planned);
    }

    [Fact]
    public void CreateSprint_StartAfterEnd_ShouldThrowDomainException()
    {
        var project = CreateProject();

        var act = () => project.CreateSprint(
            "Bad Sprint", null,
            DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            DateOnly.FromDateTime(DateTime.Today),  // end before start
            UserId);

        act.Should().Throw<DomainException>()
            .WithMessage("*start date*before*end date*");
    }

    [Fact]
    public void CreateSprint_WhenActiveSprintExists_ShouldThrowBusinessRuleViolation()
    {
        var project = CreateProject();
        var sprint  = project.CreateSprint("Sprint 1", null, null, null, UserId);
        sprint.Start(UserId);

        var act = () => project.CreateSprint("Sprint 2", null, null, null, UserId);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "SingleActiveSprint");
    }

    // ── Sprint lifecycle ──────────────────────────────────────────────────────

    [Fact]
    public void Sprint_StartAndClose_ShouldTransitionStatus()
    {
        var project = CreateProject();
        var sprint  = project.CreateSprint("Sprint 1", null, null, null, UserId);

        sprint.Status.Should().Be(SprintStatus.Planned);

        sprint.Start(UserId);
        sprint.Status.Should().Be(SprintStatus.Active);

        sprint.Close(UserId);
        sprint.Status.Should().Be(SprintStatus.Closed);
        sprint.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public void Sprint_ClosingPlannedSprint_ShouldThrowInvalidStateTransition()
    {
        var project = CreateProject();
        var sprint  = project.CreateSprint("Sprint 1", null, null, null, UserId);

        var act = () => sprint.Close(UserId);
        act.Should().Throw<InvalidStateTransitionException>();
    }

    // ── Archive ───────────────────────────────────────────────────────────────

    [Fact]
    public void Archive_ShouldSetIsArchivedAndDeletedAt()
    {
        var project = CreateProject();
        project.Archive(UserId);

        project.IsArchived.Should().BeTrue();
        project.DeletedAt.Should().NotBeNull();
        project.DeletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Archive_AlreadyArchived_ShouldThrowBusinessRuleViolation()
    {
        var project = CreateProject();
        project.Archive(UserId);

        var act = () => project.Archive(UserId);
        act.Should().Throw<BusinessRuleViolationException>();
    }
}
