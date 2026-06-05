using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Messaging;
using MassTransit;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Messaging;

public sealed class GenerateDocumentRequestedConsumer(IBackgroundDocumentGenerationProcessor processor) : IConsumer<GenerateDocumentRequested>
{
    public Task Consume(ConsumeContext<GenerateDocumentRequested> context)
        => processor.ProcessAsync(context.Message, context.CancellationToken);
}
