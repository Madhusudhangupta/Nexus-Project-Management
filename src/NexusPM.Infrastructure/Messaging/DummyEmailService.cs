using Microsoft.Extensions.Logging;
using NexusPM.Application.Common.Interfaces;

namespace NexusPM.Infrastructure.Messaging;

public sealed class DummyEmailService(ILogger<DummyEmailService> logger) : IEmailService
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("Sending dummy email to {To} with subject {Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}
