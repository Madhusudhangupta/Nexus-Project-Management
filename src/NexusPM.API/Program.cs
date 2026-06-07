using Microsoft.EntityFrameworkCore;
using NexusPM.API.Extensions;
using NexusPM.API.Hubs;
using NexusPM.API.Middleware;
using NexusPM.Application;
using NexusPM.Infrastructure;
using NexusPM.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

// ── Bootstrap Serilog before the host so startup errors are captured ──────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter())
    .CreateLogger();

try
{
    Log.Information("Starting NexusPM API");

    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog full configuration ────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Error)
        .Enrich.FromLogContext()
        .Enrich.WithCorrelationId()
        .Enrich.WithProperty("Application", "NexusPM.API")
        .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

    // ── Application + Infrastructure layers ──────────────────────────────────
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // ── API-layer services ────────────────────────────────────────────────────
    builder.Services.AddApiServices(builder.Configuration);
    builder.Services.AddSignalRServices(builder.Configuration);
    builder.Services.AddSwaggerServices();
    builder.Services.AddHealthCheckServices(builder.Configuration);
    builder.Services.AddRateLimitingServices(builder.Configuration);
    builder.Services.AddHttpContextAccessor();

    // ── Scoped services for current user/tenant resolution ────────────────────
    builder.Services.AddScoped<NexusPM.Application.Common.Interfaces.ICurrentUser, CurrentUserService>();
    builder.Services.AddScoped<NexusPM.Application.Common.Interfaces.ICurrentTenant, CurrentTenantService>();

    var app = builder.Build();

    // ── Middleware pipeline (ORDER MATTERS) ───────────────────────────────────
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        opts.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
        {
            diagCtx.Set("RequestId", httpCtx.TraceIdentifier);
            diagCtx.Set("UserId", httpCtx.User.FindFirst("sub")?.Value ?? "anonymous");
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "NexusPM API v1");
            c.RoutePrefix = "swagger";
        });
        // Apply pending migrations automatically in development
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<SecurityHeadersMiddleware>();

    app.UseHttpsRedirection();
    app.UseCors("AllowSpaOrigin");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseAuthorization();

    app.MapControllers();

    // SignalR hubs
    app.MapHub<TaskHub>("/hubs/tasks").RequireAuthorization();
    app.MapHub<NotificationHub>("/hubs/notifications").RequireAuthorization();

    // Health checks
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false  // liveness: just return 200 if process is alive
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("critical"),
        ResponseWriter = HealthCheckResponseWriter.WriteJsonAsync,
    });

    // Redirect root to Swagger UI
    app.MapGet("/", () => Results.Redirect("/swagger"));

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

return 0;
