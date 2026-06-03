using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NexusPM.Application.Common.Interfaces;
using StackExchange.Redis;
using System.Text.Json;

namespace NexusPM.Infrastructure.Caching;

/// <summary>
/// Redis-backed distributed cache service.
/// Uses System.Text.Json for serialization.
/// All operations are wrapped in try/catch so cache failures degrade gracefully
/// rather than causing request failures (cache-aside pattern).
/// </summary>
public sealed class RedisCacheService(
    IConnectionMultiplexer redis,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    private IDatabase Db => redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await Db.StringGetAsync(key);
            if (value.IsNullOrEmpty) return default;

            return JsonSerializer.Deserialize<T>((string)value!, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache GET failed for key {CacheKey}", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            await Db.StringSetAsync(key, json, ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache SET failed for key {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await Db.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache REMOVE failed for key {CacheKey}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var server = redis.GetServer(redis.GetEndPoints().First());
            var keys   = server.Keys(pattern: $"{prefix}*").ToArray();
            if (keys.Length > 0)
                await Db.KeyDeleteAsync(keys);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache REMOVE BY PREFIX failed for prefix {Prefix}", prefix);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            return await Db.KeyExistsAsync(key);
        }
        catch
        {
            return false;
        }
    }
}
