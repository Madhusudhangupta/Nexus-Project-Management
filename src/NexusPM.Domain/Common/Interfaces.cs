using MediatR;

namespace NexusPM.Domain.Common;

/// <summary>Marks entities that track creation and modification timestamps.</summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; }
    Guid CreatedById { get; }
    DateTimeOffset UpdatedAt { get; }
    Guid? UpdatedById { get; }
}

/// <summary>Marks entities that support soft deletion.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
    bool IsDeleted => DeletedAt.HasValue;
}

/// <summary>Marks entities that belong to a specific workspace tenant.</summary>
public interface ITenantEntity
{
    Guid WorkspaceId { get; }
}

/// <summary>Domain event marker interface.</summary>
public interface IDomainEvent : INotification
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

/// <summary>Base record for domain events with default EventId and timestamp.</summary>
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Typed result to avoid exceptions for expected error paths.</summary>
public sealed class Result<T>
{
    private Result(T? value, bool isSuccess, string? error)
    {
        Value = value;
        IsSuccess = isSuccess;
        Error = error;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result<T> Success(T value) => new(value, true, null);
    public static Result<T> Failure(string error) => new(default, false, error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(Error!);
}

/// <summary>Non-generic result for commands that return no value.</summary>
public sealed class Result
{
    private Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);
}

/// <summary>Paged result wrapper for list queries.</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
