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
        services.AddScoped<Abstractions.Persistence.IDocumentGenerationTaskRepository, Services.InMemoryDocumentGenerationTaskRepository>();
        services.AddSingleton<Abstractions.Storage.IDocumentArtifactStore, Services.InMemoryDocumentArtifactStore>();
        services.AddSingleton<Abstractions.Messaging.IDocumentGenerationEventPublisher, Services.NullDocumentGenerationEventPublisher>();
        services.AddScoped<Abstractions.Messaging.IBackgroundDocumentGenerationProcessor, Services.BackgroundDocumentGenerationProcessor>();
        services.AddSingleton<Abstractions.Messaging.IBackgroundGenerationScheduler, Services.InMemoryBackgroundGenerationScheduler>();
        services.AddSingleton<Services.DocumentGenerationMetrics>();
        services.AddScoped<Services.SyncDocumentGenerationService>();
        services.AddScoped<Services.AsyncDocumentGenerationService>();
        services.AddScoped<Services.TaskStatusQueryService>();
        services.AddScoped<Services.DocumentArtifactDownloadService>();

        return services;
    }
}
