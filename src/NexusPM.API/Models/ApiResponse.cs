using NexusPM.Domain.Common;

namespace NexusPM.API;

/// <summary>
/// Consistent API response envelope for all endpoints.
/// All responses have the same top-level shape regardless of content type.
/// </summary>
public sealed class ApiResponse<T>
{
    public T? Data { get; init; }
    public ApiMetadata Metadata { get; init; } = null!;
}

public sealed class ApiMetadata
{
    public string RequestId { get; init; } = null!;
    public DateTimeOffset Timestamp { get; init; }
    public string Version { get; init; } = "v1";
}

public sealed class PagedApiResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public PaginationMeta Pagination { get; init; } = null!;
}

public sealed class PaginationMeta
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public bool HasNextPage { get; init; }
    public bool HasPreviousPage { get; init; }
}

/// <summary>Static factory methods for response envelopes.</summary>
public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? requestId = null) =>
        new()
        {
            Data     = data,
            Metadata = new ApiMetadata
            {
                RequestId = requestId ?? Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                Version   = "v1",
            },
        };

    public static ApiResponse<PagedApiResponse<T>> Paged<T>(
        PagedResult<T> paged, string? requestId = null) =>
        new()
        {
            Data = new PagedApiResponse<T>
            {
                Items = paged.Items,
                Pagination = new PaginationMeta
                {
                    Page           = paged.Page,
                    PageSize       = paged.PageSize,
                    TotalCount     = paged.TotalCount,
                    TotalPages     = paged.TotalPages,
                    HasNextPage    = paged.HasNextPage,
                    HasPreviousPage = paged.HasPreviousPage,
                },
            },
            Metadata = new ApiMetadata
            {
                RequestId = requestId ?? Guid.NewGuid().ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                Version   = "v1",
            },
        };
}
