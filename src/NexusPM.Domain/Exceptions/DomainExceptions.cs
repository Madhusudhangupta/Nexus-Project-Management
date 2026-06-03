namespace NexusPM.Domain.Exceptions;

/// <summary>
/// Base exception for all domain rule violations.
/// These map to HTTP 422 Unprocessable Entity in the API layer.
/// </summary>
public class DomainException(string message) : Exception(message);

/// <summary>Raised when a requested resource does not exist or is not accessible.</summary>
public sealed class NotFoundException(string entityName, object key)
    : DomainException($"{entityName} with id '{key}' was not found.");

/// <summary>Raised when the caller lacks permission to perform an action.</summary>
public sealed class UnauthorizedException(string message)
    : DomainException(message);

/// <summary>Raised when a domain invariant is violated (e.g. circular dependency).</summary>
public sealed class BusinessRuleViolationException(string rule, string message)
    : DomainException($"Business rule '{rule}' violated: {message}")
{
    public string Rule { get; } = rule;
}

/// <summary>Raised when an invalid state transition is attempted.</summary>
public sealed class InvalidStateTransitionException(string from, string to)
    : DomainException($"Cannot transition from '{from}' to '{to}'.");

/// <summary>Raised when duplicate data violates a uniqueness constraint.</summary>
public sealed class DuplicateException(string entityName, string field, object value)
    : DomainException($"{entityName} with {field} '{value}' already exists.");
