using FluentAssertions;
using FluentValidation;
using FluentValidation.TestHelper;
using Moq;
using NexusPM.Application.Auth.Commands.Login;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Tasks.Commands.CreateTask;
using NexusPM.Domain.Aggregates.Projects;
using NexusPM.Domain.Aggregates.Tasks;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.Repositories;
using Xunit;

namespace NexusPM.Application.Tests.Tasks.Commands;

public sealed class CreateTaskCommandHandlerTests
{
    private readonly Mock<IUnitOfWork>    _uow         = new();
    private readonly Mock<ICurrentUser>   _currentUser = new();
    private readonly Mock<ICacheService>  _cache       = new();
    private readonly Mock<IProjectRepository> _projects = new();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid ProjectId   = Guid.NewGuid();
    private static readonly Guid UserId      = Guid.NewGuid();
    private static readonly Guid DefaultStateId = Guid.NewGuid();

    public CreateTaskCommandHandlerTests()
    {
        _currentUser.Setup(u => u.UserId).Returns(UserId);
        _currentUser.Setup(u => u.WorkspaceId).Returns(WorkspaceId);
        _currentUser.Setup(u => u.IsAuthenticated).Returns(true);

        // Set up UoW to return the project repository
        _uow.Setup(u => u.Projects).Returns(_projects.Object);

        var mockTaskRepo = new Mock<ITaskRepository>();
        mockTaskRepo.Setup(r => r.GetNextTaskSequenceAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _uow.Setup(u => u.Tasks).Returns(mockTaskRepo.Object);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        _cache.Setup(c => c.RemoveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateTaskAndReturnResult()
    {
        // Arrange
        var project = BuildProject();
        _projects.Setup(r => r.GetByIdWithStatesAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var handler = new CreateTaskCommandHandler(_uow.Object, _currentUser.Object, _cache.Object);
        var command = new CreateTaskCommand
        {
            WorkspaceId = WorkspaceId,
            ProjectId   = ProjectId,
            Title       = "Implement JWT authentication",
            Priority    = 2,
        };

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.TaskId.Should().NotBeEmpty();
        result.TaskKey.Should().Be("PROJ-1");
        _uow.Verify(u => u.Tasks.Add(It.IsAny<TaskItem>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ProjectNotFound_ShouldThrowNotFoundException()
    {
        _projects.Setup(r => r.GetByIdWithStatesAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Project?)null);

        var handler = new CreateTaskCommandHandler(_uow.Object, _currentUser.Object, _cache.Object);
        var command = new CreateTaskCommand
        {
            WorkspaceId = WorkspaceId,
            ProjectId   = ProjectId,
            Title       = "Test",
        };

        var act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*Project*");
    }

    [Fact]
    public async Task Handle_ProjectInWrongWorkspace_ShouldThrowUnauthorizedException()
    {
        var differentWorkspace = Guid.NewGuid();
        var project = Project.Create(differentWorkspace, "Other", "OTHER", null, UserId);
        _projects.Setup(r => r.GetByIdWithStatesAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var handler = new CreateTaskCommandHandler(_uow.Object, _currentUser.Object, _cache.Object);
        var command = new CreateTaskCommand
        {
            WorkspaceId = WorkspaceId, // different from project's workspace
            ProjectId   = ProjectId,
            Title       = "Test",
        };

        var act = async () => await handler.Handle(command, CancellationToken.None);
        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task Handle_WithStoryPoints_ShouldSetOnTask()
    {
        var project = BuildProject();
        _projects.Setup(r => r.GetByIdWithStatesAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        TaskItem? capturedTask = null;
        var mockTaskRepo = new Mock<ITaskRepository>();
        mockTaskRepo.Setup(r => r.GetNextTaskSequenceAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        mockTaskRepo.Setup(r => r.Add(It.IsAny<TaskItem>()))
            .Callback<TaskItem>(t => capturedTask = t);
        _uow.Setup(u => u.Tasks).Returns(mockTaskRepo.Object);

        var handler = new CreateTaskCommandHandler(_uow.Object, _currentUser.Object, _cache.Object);
        var command = new CreateTaskCommand
        {
            WorkspaceId = WorkspaceId,
            ProjectId   = ProjectId,
            Title       = "Task with story points",
            StoryPoints = 8,
        };

        await handler.Handle(command, CancellationToken.None);

        capturedTask.Should().NotBeNull();
        capturedTask!.StoryPoints!.Value.Should().Be(8);
        capturedTask.TaskKey.Should().Be("PROJ-5");
    }

    [Fact]
    public async Task Handle_ShouldInvalidateCacheAfterCreation()
    {
        var project = BuildProject();
        _projects.Setup(r => r.GetByIdWithStatesAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(project);

        var handler = new CreateTaskCommandHandler(_uow.Object, _currentUser.Object, _cache.Object);
        var command = new CreateTaskCommand
        {
            WorkspaceId = WorkspaceId,
            ProjectId   = ProjectId,
            Title       = "Test",
        };

        await handler.Handle(command, CancellationToken.None);

        _cache.Verify(
            c => c.RemoveByPrefixAsync(
                It.Is<string>(k => k.Contains(WorkspaceId.ToString())),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Project BuildProject()
    {
        var project = Project.Create(WorkspaceId, "Test Project", "PROJ", null, UserId);
        return project;
    }
}

// ── Validator tests ───────────────────────────────────────────────────────────

public sealed class CreateTaskCommandValidatorTests
{
    private readonly CreateTaskCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new CreateTaskCommand
        {
            WorkspaceId = Guid.NewGuid(),
            ProjectId   = Guid.NewGuid(),
            Title       = "Valid title",
            Priority    = 3,
        };

        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyTitle_ShouldHaveError()
    {
        var command = new CreateTaskCommand
        {
            WorkspaceId = Guid.NewGuid(),
            ProjectId   = Guid.NewGuid(),
            Title       = "",
        };

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Title)
              .WithErrorMessage("Title is required.");
    }

    [Fact]
    public void Validate_TitleTooLong_ShouldHaveError()
    {
        var command = new CreateTaskCommand
        {
            WorkspaceId = Guid.NewGuid(),
            ProjectId   = Guid.NewGuid(),
            Title       = new string('x', 501),
        };

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Title);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Validate_InvalidPriority_ShouldHaveError(int priority)
    {
        var command = new CreateTaskCommand
        {
            WorkspaceId = Guid.NewGuid(),
            ProjectId   = Guid.NewGuid(),
            Title       = "Valid",
            Priority    = priority,
        };

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Priority);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void Validate_NonFibonacciStoryPoints_ShouldHaveError(int points)
    {
        var command = new CreateTaskCommand
        {
            WorkspaceId = Guid.NewGuid(),
            ProjectId   = Guid.NewGuid(),
            Title       = "Valid",
            StoryPoints = points,
        };

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.StoryPoints);
    }
}

// ── Login validator tests ─────────────────────────────────────────────────────

public sealed class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ShouldHaveNoErrors()
    {
        var command = new LoginCommand
        {
            Email       = "user@example.com",
            Password    = "password123",
            WorkspaceId = Guid.NewGuid(),
        };

        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_InvalidEmail_ShouldHaveError()
    {
        var command = new LoginCommand
        {
            Email       = "not-an-email",
            Password    = "password",
            WorkspaceId = Guid.NewGuid(),
        };

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.Email);
    }

    [Fact]
    public void Validate_EmptyWorkspaceId_ShouldHaveError()
    {
        var command = new LoginCommand
        {
            Email       = "user@example.com",
            Password    = "password",
            WorkspaceId = Guid.Empty,
        };

        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(c => c.WorkspaceId);
    }
}
