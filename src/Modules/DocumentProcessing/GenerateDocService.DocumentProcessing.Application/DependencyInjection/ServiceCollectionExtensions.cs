using Microsoft.Extensions.DependencyInjection;

namespace GenerateDocService.DocumentProcessing.Application.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentProcessingApplication(this IServiceCollection services)
    {
        services.AddSingleton<Abstractions.Engines.IDocumentGenerationEngineRegistry, Services.DocumentGenerationEngineRegistry>();
        services.AddSingleton<GenerateDocService.Engine.Abstractions.ITemplateParserRegistry, Services.TemplateParserRegistry>();
        services.AddSingleton<GenerateDocService.Engine.Abstractions.ICompiledTemplateCache, Services.InMemoryCompiledTemplateCache>();
        services.AddSingleton<Abstractions.Caching.IGeneratedDocumentCache, Services.InMemoryGeneratedDocumentCache>();
        services.AddSingleton<Abstractions.Caching.IGenerationRequestDeduplicationStore, Services.InMemoryGenerationRequestDeduplicationStore>();
        services.AddSingleton<Abstractions.Persistence.IDocumentGenerationTaskRepository, Services.InMemoryDocumentGenerationTaskRepository>();
        services.AddSingleton<Abstractions.Storage.IDocumentArtifactStore, Services.InMemoryDocumentArtifactStore>();
        services.AddSingleton<Abstractions.Messaging.IDocumentGenerationEventPublisher, Services.NullDocumentGenerationEventPublisher>();
        services.AddSingleton<Abstractions.Messaging.IBackgroundDocumentGenerationProcessor, Services.BackgroundDocumentGenerationProcessor>();
        services.AddSingleton<Abstractions.Messaging.IBackgroundGenerationScheduler, Services.InMemoryBackgroundGenerationScheduler>();
        services.AddSingleton<Services.DocumentGenerationMetrics>();
        services.AddSingleton<Services.SyncDocumentGenerationService>();
        services.AddSingleton<Services.AsyncDocumentGenerationService>();
        services.AddSingleton<Services.TaskStatusQueryService>();
        services.AddSingleton<Services.DocumentArtifactDownloadService>();

        return services;
    }
}
