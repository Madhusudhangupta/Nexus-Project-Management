using Microsoft.Extensions.Options;
using NexusPM.Infrastructure.Messaging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;
using Serilog.Events;
using System.Text;
using System.Text.Json;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter())
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting NexusPM Worker");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.Configure<RabbitMqOptions>(
        builder.Configuration.GetSection(RabbitMqOptions.Section));

    builder.Services.AddSingleton<RabbitMqConnectionFactory>();
    builder.Services.AddHostedService<TaskNotificationConsumer>();
    builder.Services.AddHostedService<AuditLogConsumer>();

    builder.Host.UseSerilog((ctx, _, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
return 0;
