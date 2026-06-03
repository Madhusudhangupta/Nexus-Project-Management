using FluentAssertions;
using NexusPM.Domain.Aggregates.Workspaces;
using NexusPM.Domain.Events.Workspaces;
using NexusPM.Domain.Exceptions;
using Xunit;

namespace NexusPM.Domain.Tests.Aggregates.Projects;

public sealed class WorkspaceTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();

    private static Workspace CreateWorkspace(string name = "Acme Corp") =>
        Workspace.Create(name, OwnerId);

    // ── Creation ──────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ShouldAddOwnerAsAdmin()
    {
        var workspace = CreateWorkspace();

        workspace.Memberships.Should().ContainSingle();
        workspace.Memberships.First().UserId.Should().Be(OwnerId);
        workspace.Memberships.First().Role.Should().Be(WorkspaceRole.Admin);
    }

    [Fact]
    public void Create_ShouldGenerateSlugFromName()
    {
        var workspace = Workspace.Create("My Great Company", OwnerId);
        workspace.Slug.Value.Should().Be("my-great-company");
    }

    [Fact]
    public void Create_ShouldRaiseWorkspaceCreatedEvent()
    {
        var workspace = CreateWorkspace();
        workspace.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<WorkspaceCreatedEvent>();
    }

    [Fact]
    public void Create_NameTooLong_ShouldThrowDomainException()
    {
        var act = () => Workspace.Create(new string('x', 101), OwnerId);
        act.Should().Throw<DomainException>().WithMessage("*100*");
    }

    // ── Membership ────────────────────────────────────────────────────────────

    [Fact]
    public void InviteMember_NewUser_ShouldAddMembershipAndRaiseEvent()
    {
        var workspace = CreateWorkspace();
        var newUserId = Guid.NewGuid();
        workspace.ClearDomainEvents();

        workspace.InviteMember(newUserId, WorkspaceRole.Member, OwnerId);

        workspace.Memberships.Should().HaveCount(2);
        workspace.HasMember(newUserId).Should().BeTrue();
        workspace.GetMemberRole(newUserId).Should().Be(WorkspaceRole.Member);
        workspace.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<WorkspaceMemberInvitedEvent>();
    }

    [Fact]
    public void InviteMember_AlreadyMember_ShouldThrowDuplicateException()
    {
        var workspace = CreateWorkspace();

        var act = () => workspace.InviteMember(OwnerId, WorkspaceRole.Member, OwnerId);
        act.Should().Throw<DuplicateException>();
    }

    [Fact]
    public void RemoveMember_LastAdmin_ShouldThrowBusinessRuleViolation()
    {
        var workspace = CreateWorkspace();
        // Only one admin (owner) — cannot remove

        var act = () => workspace.RemoveMember(OwnerId, OwnerId);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "LastAdminProtection");
    }

    [Fact]
    public void RemoveMember_NonLastAdmin_ShouldSucceed()
    {
        var workspace      = CreateWorkspace();
        var secondAdminId  = Guid.NewGuid();
        var memberToRemove = Guid.NewGuid();

        workspace.InviteMember(secondAdminId, WorkspaceRole.Admin, OwnerId);
        workspace.InviteMember(memberToRemove, WorkspaceRole.Member, OwnerId);

        workspace.RemoveMember(memberToRemove, OwnerId);

        workspace.HasMember(memberToRemove).Should().BeFalse();
        workspace.Memberships.Should().HaveCount(2);
    }

    [Fact]
    public void ChangeMemberRole_ToAdminAndBack_LastAdminCannotBeDowngraded()
    {
        var workspace = CreateWorkspace();

        // Attempt to demote the only admin
        var act = () => workspace.ChangeMemberRole(OwnerId, WorkspaceRole.Member, OwnerId);
        act.Should().Throw<BusinessRuleViolationException>()
            .Where(e => e.Rule == "LastAdminProtection");
    }

    [Fact]
    public void ChangeMemberRole_WhenMultipleAdmins_ShouldSucceed()
    {
        var workspace = CreateWorkspace();
        var userId2   = Guid.NewGuid();

        workspace.InviteMember(userId2, WorkspaceRole.Admin, OwnerId);
        workspace.ChangeMemberRole(OwnerId, WorkspaceRole.Member, userId2);

        workspace.GetMemberRole(OwnerId).Should().Be(WorkspaceRole.Member);
        workspace.GetMemberRole(userId2).Should().Be(WorkspaceRole.Admin);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ShouldChangeName()
    {
        var workspace = CreateWorkspace("Old Name");
        workspace.Update("New Name", null, OwnerId);
        workspace.Name.Should().Be("New Name");
    }

    [Fact]
    public void SoftDelete_ShouldSetDeletedAt()
    {
        var workspace = CreateWorkspace();
        workspace.SoftDelete(OwnerId);

        workspace.IsDeleted.Should().BeTrue();
        workspace.DeletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }
}
