using GenerateDocService.DocumentProcessing.Application.DependencyInjection;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;
using GenerateDocService.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentProcessingInfrastructure(this IServiceCollection services)
    {
        services.AddDocumentProcessingApplication();
        services.AddSingleton<IDocumentGenerationEngine, FakeDocumentGenerationEngine>();
        return services;
    }
}
