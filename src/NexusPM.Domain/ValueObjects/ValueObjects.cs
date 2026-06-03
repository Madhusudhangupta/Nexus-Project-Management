using System.Text.RegularExpressions;
using NexusPM.Domain.Common;
using NexusPM.Domain.Exceptions;

namespace NexusPM.Domain.ValueObjects;

/// <summary>
/// Validated email address. Normalised to lowercase for case-insensitive comparison.
/// </summary>
public sealed class Email : ValueObject
{
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Value { get; }
    public string Normalized { get; }

    private Email(string value)
    {
        Value = value;
        Normalized = value.ToLowerInvariant();
    }

    public static Email Create(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        email = email.Trim();

        if (!EmailRegex.IsMatch(email))
            throw new DomainException($"'{email}' is not a valid email address.");

        if (email.Length > 320)
            throw new DomainException("Email address must not exceed 320 characters.");

        return new Email(email);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Normalized;
    }

    public override string ToString() => Value;

    public static implicit operator string(Email email) => email.Value;
}

/// <summary>
/// Task priority: 1=Critical, 2=High, 3=Medium, 4=Low, 5=None.
/// </summary>
public sealed class Priority : ValueObject
{
    public static readonly Priority Critical = new(1, "Critical");
    public static readonly Priority High     = new(2, "High");
    public static readonly Priority Medium   = new(3, "Medium");
    public static readonly Priority Low      = new(4, "Low");
    public static readonly Priority None     = new(5, "None");

    private static readonly Dictionary<int, Priority> All = new()
    {
        [1] = Critical, [2] = High, [3] = Medium, [4] = Low, [5] = None
    };

    public int Value { get; }
    public string Label { get; }

    private Priority(int value, string label) { Value = value; Label = label; }

    public static Priority FromValue(int value) =>
        All.TryGetValue(value, out var p)
            ? p
            : throw new DomainException($"'{value}' is not a valid priority. Valid values: 1-5.");

    public static Priority FromLabel(string label) =>
        All.Values.FirstOrDefault(p => p.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            ?? throw new DomainException($"'{label}' is not a valid priority label.");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Label;
}

/// <summary>
/// Fibonacci-constrained story points. Valid values: 0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89.
/// </summary>
public sealed class StoryPoints : ValueObject
{
    private static readonly HashSet<int> FibonacciValues = [0, 1, 2, 3, 5, 8, 13, 21, 34, 55, 89];

    public int Value { get; }

    private StoryPoints(int value) { Value = value; }

    public static StoryPoints Create(int value)
    {
        if (!FibonacciValues.Contains(value))
            throw new DomainException(
                $"Story points must be a Fibonacci number. Valid values: {string.Join(", ", FibonacciValues)}.");

        return new StoryPoints(value);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// URL-safe workspace slug: 2-63 lowercase alphanumeric characters and hyphens.
/// </summary>
public sealed class WorkspaceSlug : ValueObject
{
    private static readonly Regex SlugRegex = new(
        @"^[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$",
        RegexOptions.Compiled);

    public string Value { get; }

    private WorkspaceSlug(string value) { Value = value; }

    public static WorkspaceSlug Create(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        slug = slug.Trim().ToLowerInvariant();

        if (!SlugRegex.IsMatch(slug))
            throw new DomainException(
                "Workspace slug must be 2-63 lowercase alphanumeric characters or hyphens, " +
                "starting and ending with an alphanumeric character.");

        return new WorkspaceSlug(slug);
    }

    /// <summary>Generates a slug from a workspace name.</summary>
    public static WorkspaceSlug FromName(string name)
    {
        var slug = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "-")
                        .Trim('-');
        if (slug.Length < 2) slug = slug.PadRight(2, '0');
        if (slug.Length > 63) slug = slug[..63].TrimEnd('-');
        return new WorkspaceSlug(slug);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
    public static implicit operator string(WorkspaceSlug slug) => slug.Value;
}
