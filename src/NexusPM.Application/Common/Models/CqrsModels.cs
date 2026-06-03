using NexusPM.Application.Common.Models;
using MediatR;

namespace NexusPM.Application.Common.Models;

/// <summary>
/// Marker interface for commands — operations that mutate state.
/// TResult is the return type (often a created resource ID).
/// </summary>
public interface ICommand<TResult> : IRequest<TResult> { }

/// <summary>Command with no return value.</summary>
public interface ICommand : IRequest { }

/// <summary>
/// Marker interface for queries — read-only operations that return data.
/// Also implements ICacheable so the CachingBehavior can intercept.
/// </summary>
public interface IQuery<TResult> : IRequest<TResult> { }

/// <summary>
/// Marks a query as cacheable. The CachingBehavior inspects this interface
/// to determine the cache key and TTL.
/// </summary>
public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }
    bool BypassCache { get; }
}

/// <summary>
/// Application-layer result type. Wraps success/failure with error details.
/// Used by command handlers to avoid throwing exceptions for expected failures.
/// </summary>
public sealed class AppResult<T>
{
    private AppResult(T? value, bool isSuccess, string? errorCode, string? errorMessage)
    {
        Value = value;
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public T? Value { get; }
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    public static AppResult<T> Success(T value) =>
        new(value, true, null, null);

    public static AppResult<T> Failure(string errorCode, string errorMessage) =>
        new(default, false, errorCode, errorMessage);

    public static AppResult<T> NotFound(string resourceName, object id) =>
        Failure("NOT_FOUND", $"{resourceName} with id '{id}' was not found.");

    public static AppResult<T> Forbidden(string message = "Access denied.") =>
        Failure("FORBIDDEN", message);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<string, string, TOut> onFailure) =>
        IsSuccess ? onSuccess(Value!) : onFailure(ErrorCode!, ErrorMessage!);
}

/// <summary>Standard pagination parameters used across all list queries.</summary>
public sealed record PaginationRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public string? SortBy { get; init; }
    public string SortDirection { get; init; } = "desc";

    public (int page, int pageSize) Validated() =>
        (Math.Max(1, Page), Math.Clamp(PageSize, 1, 100));
}
