using Microsoft.Extensions.Options;
using NexusPM.Infrastructure.Messaging;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace NexusPM.Worker.Consumers;

/// <summary>
/// Base class for all RabbitMQ consumers.
/// Handles connection lifecycle, channel setup, DLX binding,
/// and the retry/dead-letter policy using Polly.
///
/// Subclasses implement:
///   - QueueName: the queue to consume from
///   - RoutingKey: binding key on the topic exchange
///   - ProcessAsync: the business logic for each message
/// </summary>
public abstract class RabbitMqConsumerBase(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumerBase> logger)
    : BackgroundService
{
    private readonly RabbitMqOptions _opts = options.Value;
    private IConnection? _connection;
    private IModel? _channel;

    protected abstract string QueueName   { get; }
    protected abstract string RoutingKey  { get; }
    protected virtual  int    MaxRetries  => 3;
    protected virtual  int    PrefetchCount => 10;

    protected abstract Task ProcessAsync(
        string routingKey, string messageBody, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retry connection with exponential backoff (handles RabbitMQ not ready on startup)
        var connectPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                5,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, delay, attempt, _) =>
                    logger.LogWarning(ex, "RabbitMQ connect retry {Attempt} after {Delay}s", attempt, delay.TotalSeconds));

        await connectPolicy.ExecuteAsync(async () =>
        {
            await Task.Run(() => SetupChannel(), stoppingToken);
        });

        logger.LogInformation("Consumer {Consumer} listening on queue {Queue}", GetType().Name, QueueName);

        // Keep the consumer alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private void SetupChannel()
    {
        var factory = new ConnectionFactory
        {
            HostName    = _opts.Host,
            Port        = _opts.Port,
            UserName    = _opts.Username,
            Password    = _opts.Password,
            VirtualHost = _opts.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval  = TimeSpan.FromSeconds(10),
            DispatchConsumersAsync   = true,
        };

        _connection = factory.CreateConnection($"nexuspm-worker-{QueueName}");
        _channel    = _connection.CreateModel();

        // Dead letter exchange
        const string dlx = "nexuspm.dlx";
        _channel.ExchangeDeclare(dlx, ExchangeType.Direct, durable: true);

        // Declare the queue with DLX arguments
        _channel.QueueDeclare(
            queue:       QueueName,
            durable:     true,
            exclusive:   false,
            autoDelete:  false,
            arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"]    = dlx,
                ["x-dead-letter-routing-key"] = $"{QueueName}.dead",
                ["x-message-ttl"]             = 86_400_000,  // 24 hours
            });

        // Dead letter queue
        _channel.QueueDeclare($"{QueueName}.dead", durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind($"{QueueName}.dead", dlx, $"{QueueName}.dead");

        // Bind to main exchange
        _channel.QueueBind(QueueName, _opts.ExchangeName, RoutingKey);

        // Prefetch: process N messages at a time before acknowledging
        _channel.BasicQos(0, (ushort)PrefetchCount, false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += HandleMessageAsync;
        _channel.BasicConsume(QueueName, autoAck: false, consumer: consumer);
    }

    private async Task HandleMessageAsync(object _, BasicDeliverEventArgs ea)
    {
        var body       = Encoding.UTF8.GetString(ea.Body.ToArray());
        var routingKey = ea.RoutingKey;

        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                MaxRetries,
                attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                (ex, delay, attempt, _) =>
                    logger.LogWarning(ex,
                        "Message processing retry {Attempt} for queue {Queue}", attempt, QueueName));

        try
        {
            await retryPolicy.ExecuteAsync(
                ct => ProcessAsync(routingKey, body, ct),
                CancellationToken.None);

            _channel!.BasicAck(ea.DeliveryTag, multiple: false);
            logger.LogDebug("Message ACKed from queue {Queue}", QueueName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Message failed after {MaxRetries} retries. Sending to DLQ. Queue: {Queue}",
                MaxRetries, QueueName);

            // Reject without requeue — sends to DLX
            _channel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public override void Dispose()
    {
        _channel?.Close();
        _connection?.Close();
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}

// ── Task Notification Consumer ────────────────────────────────────────────────

/// <summary>
/// Consumes task-related events and triggers email/push notifications.
/// Handles: task.assigned, task.created, task.status_changed
/// </summary>
public sealed class TaskNotificationConsumer(
    IOptions<RabbitMqOptions> options,
    ILogger<TaskNotificationConsumer> logger)
    : RabbitMqConsumerBase(options, logger)
{
    protected override string QueueName  => "notifications.task-events";
    protected override string RoutingKey => "task.*";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected override async Task ProcessAsync(
        string routingKey, string messageBody, CancellationToken ct)
    {
        logger.LogInformation(
            "Processing {RoutingKey} event: {Body}",
            routingKey, messageBody[..Math.Min(200, messageBody.Length)]);

        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(messageBody, JsonOpts);
        if (payload is null) return;

        switch (routingKey)
        {
            case "task.assigned":
                await HandleTaskAssignedAsync(payload, ct);
                break;
            case "task.created":
                await HandleTaskCreatedAsync(payload, ct);
                break;
            case "task.status_changed":
                await HandleTaskStatusChangedAsync(payload, ct);
                break;
            default:
                logger.LogWarning("Unknown routing key: {RoutingKey}", routingKey);
                break;
        }
    }

    private Task HandleTaskAssignedAsync(
        Dictionary<string, JsonElement> payload, CancellationToken ct)
    {
        // In production: load assignee email from DB, render email template, send via SendGrid
        var taskId     = payload.TryGetValue("taskId", out var t) ? t.GetString() : "unknown";
        var assigneeId = payload.TryGetValue("assigneeId", out var a) ? a.GetString() : "unknown";

        logger.LogInformation(
            "Task {TaskId} assigned to user {AssigneeId} — sending notification",
            taskId, assigneeId);

        return Task.CompletedTask;
    }

    private Task HandleTaskCreatedAsync(
        Dictionary<string, JsonElement> payload, CancellationToken ct)
    {
        logger.LogInformation("Task created event processed");
        return Task.CompletedTask;
    }

    private Task HandleTaskStatusChangedAsync(
        Dictionary<string, JsonElement> payload, CancellationToken ct)
    {
        logger.LogInformation("Task status changed event processed");
        return Task.CompletedTask;
    }
}

// ── Audit Log Consumer ────────────────────────────────────────────────────────

/// <summary>
/// Consumes all domain events and writes immutable audit log entries.
/// Routes: task.*, project.*, workspace.*
/// </summary>
public sealed class AuditLogConsumer(
    IOptions<RabbitMqOptions> options,
    ILogger<AuditLogConsumer> logger)
    : RabbitMqConsumerBase(options, logger)
{
    protected override string QueueName  => "audit.all-events";
    protected override string RoutingKey => "#";   // wildcard: receive everything

    protected override async Task ProcessAsync(
        string routingKey, string messageBody, CancellationToken ct)
    {
        logger.LogInformation(
            "Audit: {RoutingKey} — {Preview}",
            routingKey, messageBody[..Math.Min(300, messageBody.Length)]);

        // In production: parse payload, write to audit_log_entries table
        await Task.CompletedTask;
    }
}
