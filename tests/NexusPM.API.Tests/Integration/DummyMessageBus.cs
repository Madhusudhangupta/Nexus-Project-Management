using NexusPM.Application.Common.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace NexusPM.API.Tests.Integration;

public class DummyMessageBus : IMessageBus
{
    public Task PublishAsync<T>(T message, string? routingKey = null, CancellationToken ct = default) where T : class
    {
        return Task.CompletedTask;
    }
}
