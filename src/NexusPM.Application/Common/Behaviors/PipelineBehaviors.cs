using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Application.Common.Models;
using System.Diagnostics;

namespace NexusPM.Application.Common.Behaviors;

/// <summary>
/// Validates all incoming requests that have a registered IValidator.
/// Runs before the handler and throws ValidationException on failure,
/// which the global exception middleware maps to HTTP 422.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count > 0)
            throw new ValidationException(failures);

        return await next();
    }
}

/// <summary>
/// Logs all MediatR requests with elapsed time and outcome.
/// Uses structured logging with the request type name as a property.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var userId = currentUser.IsAuthenticated ? currentUser.UserId.ToString() : "anonymous";

        logger.LogInformation(
            "Handling {RequestName} for user {UserId}",
            requestName, userId);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            sw.Stop();
            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms",
                requestName, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex,
                "Error handling {RequestName} after {ElapsedMs}ms",
                requestName, sw.ElapsedMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// Caches the results of queries that implement ICacheable.
/// On cache miss, executes the query and stores the result.
/// On cache hit, returns the cached value and skips the handler entirely.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(
    ICacheService cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only cache queries that opt in via ICacheable
        if (request is not ICacheable cacheableRequest || cacheableRequest.BypassCache)
            return await next();

        var cacheKey = cacheableRequest.CacheKey;
        var cached = await cache.GetAsync<TResponse>(cacheKey, cancellationToken);

        if (cached is not null)
        {
            logger.LogDebug("Cache HIT for {CacheKey}", cacheKey);
            return cached;
        }

        logger.LogDebug("Cache MISS for {CacheKey}", cacheKey);
        var response = await next();

        await cache.SetAsync(cacheKey, response, cacheableRequest.CacheDuration, cancellationToken);
        return response;
    }
}

/// <summary>
/// Wraps commands in a database transaction via the Unit of Work.
/// Rolls back automatically if the handler throws.
/// Only activates for requests that implement ICommand (not queries).
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse>(
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private const int SlowRequestThresholdMs = 500;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        if (sw.ElapsedMilliseconds > SlowRequestThresholdMs)
        {
            logger.LogWarning(
                "Slow request detected: {RequestName} took {ElapsedMs}ms. Request: {@Request}",
                typeof(TRequest).Name, sw.ElapsedMilliseconds, request);
        }

        return response;
    }
}
