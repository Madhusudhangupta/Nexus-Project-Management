using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NexusPM.Application.Common.Interfaces;
using NexusPM.Domain.Repositories;
using NexusPM.Infrastructure.Authentication;
using NexusPM.Infrastructure.Caching;
using NexusPM.Infrastructure.Messaging;
using NexusPM.Infrastructure.Persistence;
using NexusPM.Infrastructure.Persistence.Repositories;
using StackExchange.Redis;
using System.Text;

namespace NexusPM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Database ──────────────────────────────────────────────────────────
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection"),
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    npgsql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), null);
                    npgsql.CommandTimeout(30);
                })
            .EnableSensitiveDataLogging(false)
            .EnableDetailedErrors(false));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Redis ─────────────────────────────────────────────────────────────
        var redisConnectionString = configuration.GetConnectionString("Redis")!;
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton<ICacheService, RedisCacheService>();

        // ── Authentication ────────────────────────────────────────────────────
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        var jwtOptions = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()!;
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ValidateIssuer   = true,
                    ValidIssuer      = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience    = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew        = TimeSpan.Zero,
                };

                // Allow JWT via query string for SignalR connections
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"];
                        var path        = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                            ctx.Token = accessToken;
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("WorkspaceAccess", policy =>
                policy.RequireClaim("workspace_id"))
            .AddPolicy("AdminOnly", policy =>
                policy.RequireClaim("role", "Admin"))
            .AddPolicy("MemberOrAbove", policy =>
                policy.RequireClaim("role", "Admin", "Member"));

        // ── Messaging ─────────────────────────────────────────────────────────
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Section));
        services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
        services.AddTransient<IEmailService, DummyEmailService>();

        // ── DateTime provider ─────────────────────────────────────────────────
        services.AddSingleton<IDateTimeProvider, UtcDateTimeProvider>();

        return services;
    }
}

internal sealed class UtcDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow  => DateTimeOffset.UtcNow;
    public DateOnly TodayUtc      => DateOnly.FromDateTime(DateTime.UtcNow);
}
