using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusPM.Application.Common.Interfaces;
using Polly;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace NexusPM.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMQ";
    public required string Host { get; init; }
    public int Port { get; init; } = 5672;
    public required string Username { get; init; }
    public required string Password { get; init; }
    public string VirtualHost { get; init; } = "/";
    public string ExchangeName { get; init; } = "nexuspm.events";
}

/// <summary>
/// RabbitMQ message bus publisher.
/// Uses a topic exchange so consumers can subscribe to specific routing keys
/// (e.g. "task.assigned", "task.status_changed").
///
/// Publisher confirms are enabled so we know the broker received the message.
/// Polly retry policy handles transient connectivity failures.
/// </summary>
public sealed class RabbitMqMessageBus : IMessageBus, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqMessageBus> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RabbitMqMessageBus(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqMessageBus> logger)
    {
        _options = options.Value;
        _logger  = logger;

        var factory = new ConnectionFactory
        {
            HostName    = _options.Host,
            Port        = _options.Port,
            UserName    = _options.Username,
            Password    = _options.Password,
            VirtualHost = _options.VirtualHost,
            // Automatic recovery on connection loss
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval  = TimeSpan.FromSeconds(10),
        };

        _connection = factory.CreateConnection("nexuspm-api-publisher");
        _channel    = _connection.CreateModel();

        // Declare the topic exchange (idempotent)
        _channel.ExchangeDeclare(
            exchange:   _options.ExchangeName,
            type:       ExchangeType.Topic,
            durable:    true,
            autoDelete: false);

        // Enable publisher confirms for at-least-once delivery
        _channel.ConfirmSelect();
    }

    public async Task PublishAsync<T>(T message, string? routingKey = null, CancellationToken ct = default)
        where T : class
    {
        var retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning(ex, "RabbitMQ publish retry {Attempt} after {Delay}", attempt, delay));

        await retryPolicy.ExecuteAsync(async () =>
        {
            var body       = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));
            var properties = _channel.CreateBasicProperties();
            properties.Persistent    = true;   // survive broker restart
            properties.ContentType   = "application/json";
            properties.MessageId     = Guid.NewGuid().ToString();
            properties.Timestamp     = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            var key = routingKey ?? typeof(T).Name.ToLowerInvariant();

            _channel.BasicPublish(
                exchange:   _options.ExchangeName,
                routingKey: key,
                mandatory:  false,
                basicProperties: properties,
                body:       body);

            // Wait for broker acknowledgement (publisher confirms)
            if (!_channel.WaitForConfirms(timeout: TimeSpan.FromSeconds(5)))
                throw new InvalidOperationException("RabbitMQ broker did not confirm message.");

            _logger.LogDebug(
                "Published {MessageType} to exchange {Exchange} with routing key {RoutingKey}",
                typeof(T).Name, _options.ExchangeName, key);

            await Task.CompletedTask;
        });
    }

    public void Dispose()
    {
        _channel.Close();
        _connection.Close();
        _channel.Dispose();
        _connection.Dispose();
    }
}
