using Microsoft.Extensions.DependencyInjection;

namespace GenerateDocService.DocumentProcessing.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentProcessingApplication(this IServiceCollection services)
    {
        services.AddSingleton<Abstractions.Engines.IDocumentGenerationEngineRegistry, Services.DocumentGenerationEngineRegistry>();
        services.AddSingleton<Abstractions.Persistence.IDocumentGenerationTaskRepository, Services.InMemoryDocumentGenerationTaskRepository>();
        services.AddSingleton<Abstractions.Messaging.IBackgroundGenerationScheduler, Services.InMemoryBackgroundGenerationScheduler>();
        services.AddSingleton<Services.SyncDocumentGenerationService>();
        services.AddSingleton<Services.AsyncDocumentGenerationService>();
        services.AddSingleton<Services.TaskStatusQueryService>();

        return services;
    }
}
