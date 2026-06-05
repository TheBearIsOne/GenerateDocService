namespace GenerateDocService.DocumentProcessing.Infrastructure.Messaging;

public sealed class DocumentProcessingMessagingOptions
{
    public const string SectionName = "DocumentProcessing:Messaging";

    public string Transport { get; set; } = "InMemory";

    public string QueueName { get; set; } = "document-generation";

    public RabbitMqMessagingOptions RabbitMq { get; set; } = new();

    public bool IsRabbitMqTransport()
        => string.Equals(Transport, "RabbitMQ", StringComparison.OrdinalIgnoreCase);
}

public sealed class RabbitMqMessagingOptions
{
    public string Host { get; set; } = "localhost";

    public ushort Port { get; set; } = 5672;

    public string VirtualHost { get; set; } = "/";

    public string Username { get; set; } = "guest";

    public string Password { get; set; } = "guest";
}
