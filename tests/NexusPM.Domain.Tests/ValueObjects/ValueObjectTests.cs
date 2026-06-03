using FluentAssertions;
using NexusPM.Domain.Exceptions;
using NexusPM.Domain.ValueObjects;
using Xunit;

namespace NexusPM.Domain.Tests.ValueObjects;

public sealed class EmailTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user.name+tag@sub.domain.co.uk")]
    [InlineData("USER@EXAMPLE.COM")]
    public void Create_ValidEmail_ShouldSucceed(string email)
    {
        var act = () => Email.Create(email);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("@domain.com")]
    [InlineData("user@")]
    [InlineData("")]
    public void Create_InvalidEmail_ShouldThrowDomainException(string email)
    {
        var act = () => Email.Create(email);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Create_ShouldNormalizeTolowercase()
    {
        var email = Email.Create("User@EXAMPLE.COM");
        email.Normalized.Should().Be("user@example.com");
        email.Value.Should().Be("User@EXAMPLE.COM"); // original casing preserved
    }

    [Fact]
    public void TwoEmailsWithSameAddress_ShouldBeEqual()
    {
        var a = Email.Create("user@example.com");
        var b = Email.Create("USER@example.com");
        a.Should().Be(b); // ValueObject equality by normalized form
    }

    [Fact]
    public void TwoEmailsWithDifferentAddresses_ShouldNotBeEqual()
    {
        var a = Email.Create("alice@example.com");
        var b = Email.Create("bob@example.com");
        a.Should().NotBe(b);
    }
}

public sealed class PriorityTests
{
    [Theory]
    [InlineData(1, "Critical")]
    [InlineData(2, "High")]
    [InlineData(3, "Medium")]
    [InlineData(4, "Low")]
    [InlineData(5, "None")]
    public void FromValue_ValidValue_ShouldReturnCorrectPriority(int value, string label)
    {
        var priority = Priority.FromValue(value);
        priority.Value.Should().Be(value);
        priority.Label.Should().Be(label);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void FromValue_InvalidValue_ShouldThrowDomainException(int value)
    {
        var act = () => Priority.FromValue(value);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SamePriorityValues_ShouldBeEqual()
    {
        Priority.High.Should().Be(Priority.FromValue(2));
    }

    [Fact]
    public void DifferentPriorityValues_ShouldNotBeEqual()
    {
        Priority.High.Should().NotBe(Priority.Low);
    }
}

public sealed class StoryPointsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(13)]
    [InlineData(21)]
    [InlineData(34)]
    [InlineData(55)]
    [InlineData(89)]
    public void Create_ValidFibonacciValue_ShouldSucceed(int value)
    {
        var sp = StoryPoints.Create(value);
        sp.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(100)]
    public void Create_NonFibonacciValue_ShouldThrowDomainException(int value)
    {
        var act = () => StoryPoints.Create(value);
        act.Should().Throw<DomainException>()
            .WithMessage("*Fibonacci*");
    }
}

public sealed class WorkspaceSlugTests
{
    [Theory]
    [InlineData("my-workspace")]
    [InlineData("abc")]
    [InlineData("workspace123")]
    [InlineData("my-great-team-2024")]
    public void Create_ValidSlug_ShouldSucceed(string slug)
    {
        var act = () => WorkspaceSlug.Create(slug);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("a")]         // too short
    [InlineData("-starts")]   // starts with hyphen
    [InlineData("ends-")]     // ends with hyphen
    [InlineData("has space")] // contains space
    [InlineData("HAS_UPPER")] // uppercase
    public void Create_InvalidSlug_ShouldThrowDomainException(string slug)
    {
        var act = () => WorkspaceSlug.Create(slug);
        act.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData("My Workspace", "my-workspace")]
    [InlineData("Acme Corp!", "acme-corp")]
    [InlineData("Hello   World", "hello-world")]
    public void FromName_ShouldGenerateValidSlug(string name, string expectedSlug)
    {
        var slug = WorkspaceSlug.FromName(name);
        slug.Value.Should().Be(expectedSlug);
    }
}
