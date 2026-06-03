using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace NexusPM.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddControllers()
            .AddJsonOptions(opts =>
            {
                opts.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                opts.JsonSerializerOptions.DefaultIgnoreCondition =
                    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });

        services.AddCors(opts =>
        {
            opts.AddPolicy("AllowSpaOrigin", policy =>
            {
                var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                              ?? ["http://localhost:3000"];
                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();  // required for SignalR
            });
        });

        services.AddEndpointsApiExplorer();

        return services;
    }

    public static IServiceCollection AddSignalRServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")!;

        services.AddSignalR(opts =>
        {
            opts.EnableDetailedErrors       = false;
            opts.MaximumReceiveMessageSize  = 32 * 1024; // 32 KB
            opts.KeepAliveInterval          = TimeSpan.FromSeconds(15);
            opts.ClientTimeoutInterval      = TimeSpan.FromSeconds(30);
        })
        .AddStackExchangeRedis(redisConnectionString, opts =>
        {
            opts.Configuration.ChannelPrefix =
                StackExchange.Redis.RedisChannel.Literal("nexuspm");
        });

        services.AddScoped<NexusPM.Application.Common.Interfaces.IRealtimeService, NexusPM.API.Hubs.SignalRRealtimeService>();

        return services;
    }

    public static IServiceCollection AddSwaggerServices(this IServiceCollection services)
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title       = "NexusPM API",
                Version     = "v1",
                Description = "Multi-Tenant SaaS Project Management Platform",
                Contact = new OpenApiContact
                {
                    Name  = "NexusPM Engineering",
                    Email = "api@nexuspm.io",
                },
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name         = "Authorization",
                Type         = SecuritySchemeType.Http,
                Scheme       = "bearer",
                BearerFormat = "JWT",
                In           = ParameterLocation.Header,
                Description  = "Enter JWT token",
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id   = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Include XML documentation
            var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
                c.IncludeXmlComments(xmlPath);
        });

        return services;
    }

    public static IServiceCollection AddHealthCheckServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddNpgSql(
                configuration.GetConnectionString("DefaultConnection")!,
                name: "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["critical", "db"])
            .AddRedis(
                configuration.GetConnectionString("Redis")!,
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: ["cache"]);

        return services;
    }

    public static IServiceCollection AddRateLimitingServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRateLimiter(opts =>
        {
            opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Global sliding window: 300 requests per minute per user
            opts.AddSlidingWindowLimiter("authenticated", limiterOpts =>
            {
                limiterOpts.PermitLimit       = 300;
                limiterOpts.Window            = TimeSpan.FromMinutes(1);
                limiterOpts.SegmentsPerWindow = 6;
                limiterOpts.QueueLimit        = 0;
            });

            // Stricter limit for auth endpoints: 5 per minute per IP
            opts.AddSlidingWindowLimiter("auth", limiterOpts =>
            {
                limiterOpts.PermitLimit       = 5;
                limiterOpts.Window            = TimeSpan.FromMinutes(1);
                limiterOpts.SegmentsPerWindow = 6;
                limiterOpts.QueueLimit        = 0;
            });
        });

        return services;
    }
}

public static class HealthCheckResponseWriter
{
    public static async Task WriteJsonAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(new
        {
            status  = report.Status.ToString(),
            results = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status      = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration    = e.Value.Duration.TotalMilliseconds,
                }),
            totalDuration = report.TotalDuration.TotalMilliseconds,
        });
        await context.Response.WriteAsync(json);
    }
}
