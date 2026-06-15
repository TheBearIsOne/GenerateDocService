using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.DependencyInjection;
using GenerateDocService.DocumentProcessing.Infrastructure.HealthChecks;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;
using GenerateDocService.DocumentProcessing.Infrastructure.Caching;
using GenerateDocService.DocumentProcessing.Infrastructure.Messaging;
using GenerateDocService.DocumentProcessing.Infrastructure.Storage;
using GenerateDocService.DocumentProcessing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using GenerateDocService.Engine.Abstractions;
using GenerateDocService.Engine.DotLiquid;
using GenerateDocService.Engine.QuestPdf;
using GenerateDocService.Engine.Scriban;
using MassTransit;
using Minio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using System.Reflection;

namespace GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentProcessingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDocumentProcessingApplication();
        services.AddTemplateParsers(
            typeof(ScribanTemplateParser).Assembly,
            typeof(DotLiquidTemplateParser).Assembly);
        services.AddDocumentGenerationEngines(
            typeof(FakeDocumentGenerationEngine).Assembly,
            typeof(ScribanDocumentGenerationEngine).Assembly,
            typeof(QuestPdfDocumentGenerationEngine).Assembly,
            typeof(DotLiquidDocumentGenerationEngine).Assembly);
        services.AddDocumentProcessingCaching(configuration);
        services.AddDocumentProcessingStorage(configuration);
        services.AddDocumentProcessingMessaging(configuration);
        services.AddDocumentProcessingPersistence(configuration);
        services.AddDocumentProcessingHealthChecks(configuration);
        return services;
    }

    public static IServiceCollection AddTemplateParsers(this IServiceCollection services, params Assembly[] assemblies)
    {
        var targetAssemblies = assemblies.Length > 0
            ? assemblies.Distinct().ToArray()
            : [Assembly.GetExecutingAssembly()];

        var parserTypes = targetAssemblies
            .SelectMany(static assembly => assembly.DefinedTypes)
            .Where(static type =>
                type is { IsAbstract: false, IsInterface: false } &&
                typeof(ITemplateParser).IsAssignableFrom(type.AsType()))
            .Select(static type => type.AsType())
            .ToArray();

        foreach (var parserType in parserTypes)
        {
            services.AddSingleton(parserType);
            services.AddSingleton(typeof(ITemplateParser), serviceProvider =>
            {
                if (parserType == typeof(ScribanTemplateParser))
                {
                    var cache = serviceProvider.GetRequiredService<ICompiledTemplateCache>();
                    return new ScribanTemplateParser(cache);
                }

                if (parserType == typeof(DotLiquidTemplateParser))
                {
                    var cache = serviceProvider.GetRequiredService<ICompiledTemplateCache>();
                    return new DotLiquidTemplateParser(cache);
                }

                return (ITemplateParser)serviceProvider.GetRequiredService(parserType);
            });
        }

        return services;
    }

    public static IServiceCollection AddDocumentGenerationEngines(this IServiceCollection services, params Assembly[] assemblies)
    {
        var targetAssemblies = assemblies.Length > 0
            ? assemblies.Distinct().ToArray()
            : [Assembly.GetExecutingAssembly()];

        var engineTypes = targetAssemblies
            .SelectMany(static assembly => assembly.DefinedTypes)
            .Where(static type =>
                type is { IsAbstract: false, IsInterface: false } &&
                typeof(IDocumentGenerationEngine).IsAssignableFrom(type.AsType()) &&
                type.GetCustomAttribute<DocumentEngineAttribute>() is not null)
            .Select(static type => type.AsType())
            .ToArray();

        foreach (var engineType in engineTypes)
        {
            services.AddSingleton(engineType);
            services.AddSingleton(typeof(IDocumentGenerationEngine), serviceProvider =>
                (IDocumentGenerationEngine)serviceProvider.GetRequiredService(engineType));
        }

        return services;
    }

    private static IServiceCollection AddDocumentProcessingCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentProcessingCacheOptions>(
            configuration.GetSection(DocumentProcessingCacheOptions.SectionName));

        var cacheOptions = configuration
            .GetSection(DocumentProcessingCacheOptions.SectionName)
            .Get<DocumentProcessingCacheOptions>()
            ?? new DocumentProcessingCacheOptions();

        if (!cacheOptions.IsRedisProvider())
        {
            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(cacheOptions.Redis.ConnectionString));

        services.Replace(ServiceDescriptor.Singleton<ICompiledTemplateCache, RedisCompiledTemplateCache>());
        services.Replace(ServiceDescriptor.Singleton<IGeneratedDocumentCache, RedisGeneratedDocumentCache>());
        services.Replace(ServiceDescriptor.Singleton<IGenerationRequestDeduplicationStore, RedisGenerationRequestDeduplicationStore>());

        return services;
    }

    private static IServiceCollection AddDocumentProcessingStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentProcessingStorageOptions>(
            configuration.GetSection(DocumentProcessingStorageOptions.SectionName));

        var storageOptions = configuration
            .GetSection(DocumentProcessingStorageOptions.SectionName)
            .Get<DocumentProcessingStorageOptions>()
            ?? new DocumentProcessingStorageOptions();

        if (!storageOptions.IsObjectStorageProvider())
        {
            return services;
        }

        services.AddSingleton<IMinioClient>(_ =>
        {
            var endpoint = BuildMinioEndpoint(storageOptions.ObjectStorage.Endpoint);
            return new MinioClient()
                .WithEndpoint(endpoint.Host, endpoint.Port)
                .WithCredentials(storageOptions.ObjectStorage.AccessKey, storageOptions.ObjectStorage.SecretKey)
                .WithSSL(storageOptions.ObjectStorage.UseSsl)
                .Build();
        });

        services.Replace(ServiceDescriptor.Singleton<IDocumentArtifactStore, MinioDocumentArtifactStore>());

        return services;
    }

    private static Uri BuildMinioEndpoint(string endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri;
        }

        return new Uri($"http://{endpoint}");
    }

    private static IServiceCollection AddDocumentProcessingMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentProcessingMessagingOptions>(
            configuration.GetSection(DocumentProcessingMessagingOptions.SectionName));

        var messagingOptions = configuration
            .GetSection(DocumentProcessingMessagingOptions.SectionName)
            .Get<DocumentProcessingMessagingOptions>()
            ?? new DocumentProcessingMessagingOptions();

        services.AddMassTransit(busRegistrationConfigurator =>
        {
            busRegistrationConfigurator.AddConsumer<GenerateDocumentRequestedConsumer>();

            if (messagingOptions.IsRabbitMqTransport())
            {
                busRegistrationConfigurator.UsingRabbitMq((context, cfg) =>
                {
                    cfg.Host(
                        messagingOptions.RabbitMq.Host,
                        messagingOptions.RabbitMq.Port,
                        messagingOptions.RabbitMq.VirtualHost,
                        hostConfigurator =>
                        {
                            hostConfigurator.Username(messagingOptions.RabbitMq.Username);
                            hostConfigurator.Password(messagingOptions.RabbitMq.Password);
                        });

                    cfg.ConfigureEndpoints(context);
                });

                return;
            }

            busRegistrationConfigurator.UsingInMemory((context, cfg) =>
            {
                cfg.ConfigureEndpoints(context);
            });
        });

        services.Replace(ServiceDescriptor.Singleton<IBackgroundGenerationScheduler, MassTransitBackgroundGenerationScheduler>());
        services.Replace(ServiceDescriptor.Singleton<IDocumentGenerationEventPublisher, MassTransitDocumentGenerationEventPublisher>());

        return services;
    }

    private static IServiceCollection AddDocumentProcessingPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DocumentProcessingPersistenceOptions>(
            configuration.GetSection(DocumentProcessingPersistenceOptions.SectionName));
        services.Configure<ArtifactRetentionOptions>(
            configuration.GetSection(ArtifactRetentionOptions.SectionName));

        var persistenceOptions = configuration
            .GetSection(DocumentProcessingPersistenceOptions.SectionName)
            .Get<DocumentProcessingPersistenceOptions>()
            ?? new DocumentProcessingPersistenceOptions();

        if (!persistenceOptions.IsPostgreSqlProvider())
        {
            services.AddHostedService<ArtifactCleanupBackgroundService>();
            return services;
        }

        services.AddDbContext<DocumentGenerationDbContext>(options =>
            options.UseNpgsql(persistenceOptions.PostgreSql.ConnectionString));

        services.Replace(
            ServiceDescriptor.Singleton<
                Application.Abstractions.Persistence.IDocumentGenerationTaskRepository,
                PostgreSqlDocumentGenerationTaskRepository>());

        services.AddHostedService<ArtifactCleanupBackgroundService>();

        return services;
    }

    private static IServiceCollection AddDocumentProcessingHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var cacheOptions = configuration
            .GetSection(DocumentProcessingCacheOptions.SectionName)
            .Get<DocumentProcessingCacheOptions>()
            ?? new DocumentProcessingCacheOptions();

        var persistenceOptions = configuration
            .GetSection(DocumentProcessingPersistenceOptions.SectionName)
            .Get<DocumentProcessingPersistenceOptions>()
            ?? new DocumentProcessingPersistenceOptions();

        var messagingOptions = configuration
            .GetSection(DocumentProcessingMessagingOptions.SectionName)
            .Get<DocumentProcessingMessagingOptions>()
            ?? new DocumentProcessingMessagingOptions();

        var storageOptions = configuration
            .GetSection(DocumentProcessingStorageOptions.SectionName)
            .Get<DocumentProcessingStorageOptions>()
            ?? new DocumentProcessingStorageOptions();

        var healthChecks = services.AddHealthChecks();

        healthChecks.AddCheck<DocumentGenerationEnginesHealthCheck>(
            "document-engines",
            tags: ["live", "ready"]);

        if (cacheOptions.IsRedisProvider())
        {
            healthChecks.AddCheck<RedisConnectionHealthCheck>(
                "redis-cache",
                tags: ["ready"]);
        }
        else
        {
            healthChecks.AddCheck(
                "cache-provider",
                () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("In-memory cache provider configured."),
                tags: ["live", "ready"]);
        }

        if (persistenceOptions.IsPostgreSqlProvider())
        {
            healthChecks.AddCheck(
                "postgresql-persistence",
                () => new HealthCheckResult(
                    HealthStatus.Healthy,
                    "PostgreSQL persistence configured."),
                tags: ["ready"]);
        }
        else
        {
            healthChecks.AddCheck(
                "task-persistence",
                () => HealthCheckResult.Healthy("In-memory task persistence configured."),
                tags: ["live", "ready"]);
        }

        healthChecks.AddCheck(
            "authentication",
            () => HealthCheckResult.Healthy("Authentication configured."),
            tags: ["live", "ready"]);

        if (messagingOptions.IsRabbitMqTransport())
        {
            healthChecks.AddCheck(
                "rabbitmq-transport",
                new TcpEndpointHealthCheck(messagingOptions.RabbitMq.Host, messagingOptions.RabbitMq.Port),
                tags: ["ready"]);
        }
        else
        {
            healthChecks.AddCheck(
                "messaging-transport",
                () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("In-memory messaging transport configured."),
                tags: ["live", "ready"]);
        }

        if (storageOptions.IsObjectStorageProvider())
        {
            var endpoint = BuildMinioEndpoint(storageOptions.ObjectStorage.Endpoint);
            healthChecks.AddCheck(
                "object-storage",
                new TcpEndpointHealthCheck(endpoint.Host, endpoint.Port),
                tags: ["ready"]);
        }
        else
        {
            healthChecks.AddCheck(
                "artifact-storage",
                () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("In-memory artifact storage configured."),
                tags: ["live", "ready"]);
        }

        return services;
    }
}
