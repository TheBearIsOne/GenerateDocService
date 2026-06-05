using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using MassTransit;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Messaging;

public sealed class MassTransitBackgroundGenerationScheduler(
    IBus bus,
    IOptions<DocumentProcessingMessagingOptions> options) : IBackgroundGenerationScheduler
{
    public async Task<string> EnqueueAsync(GenerateDocumentRequested message, CancellationToken cancellationToken = default)
    {
        if (options.Value.IsRabbitMqTransport())
        {
            var endpoint = await bus.GetSendEndpoint(new Uri($"queue:{options.Value.QueueName}"));
            await endpoint.Send(message, cancellationToken);
            return message.TaskId;
        }

        await bus.Publish(message, cancellationToken);
        return message.TaskId;
    }
}
