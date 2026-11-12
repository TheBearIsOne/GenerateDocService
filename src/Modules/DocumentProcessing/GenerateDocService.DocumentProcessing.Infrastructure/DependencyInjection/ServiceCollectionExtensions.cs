using GenerateDocService.DocumentProcessing.Application.Abstractions.Caching;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Messaging;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Storage;
using GenerateDocService.DocumentProcessing.Application.DependencyInjection;
using GenerateDocService.DocumentProcessing.Application.Resilience;
using GenerateDocService.DocumentProcessing.Infrastructure.HealthChecks;
using GenerateDocService.DocumentProcessing.Infrastructure.Engines;
using GenerateDocService.DocumentProcessing.Infrastructure.Caching;
using GenerateDocService.DocumentProcessing.Infrastructure.Messaging;
using GenerateDocService.DocumentProcessing.Infrastructure.Resilience;
using GenerateDocService.DocumentProcessing.Infrastructure.Storage;
using GenerateDocService.DocumentProcessing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Npgsql;
using Polly;
using GenerateDocService.Engine.Abstractions;
using GenerateDocService.Engine.Api;
using GenerateDocService.Engine.DotLiquid;
using GenerateDocService.Engine.Fop;
using GenerateDocService.Engine.MiniExcel;
using GenerateDocService.Engine.MiniPdf;
using GenerateDocService.Engine.MiniWord;
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
        services.AddDocumentProcessingResilience(configuration);
        services.AddDocumentProcessingApiTargets(configuration);
        services.AddTemplateParsers(
            typeof(ScribanTemplateParser).Assembly,
            typeof(DotLiquidTemplateParser).Assembly);
        services.AddDocumentGenerationEngines(
            typeof(FakeDocumentGenerationEngine).Assembly,
            typeof(ApiDocumentGenerationEngine).Assembly,
            typeof(ScribanDocumentGenerationEngine).Assembly,
            typeof(QuestPdfDocumentGenerationEngine).Assembly,
            typeof(DotLiquidDocumentGenerationEngine).Assembly,
            typeof(MiniExcelDocumentGenerationEngine).Assembly,
            typeof(MiniWordDocumentGenerationEngine).Assembly,
            typeof(MiniPdfDocumentGenerationEngine).Assembly,
            typeof(FopDocumentGenerationEngine).Assembly);
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
            if (engineType == typeof(FopDocumentGenerationEngine))
            {
                services.AddSingleton(engineType, serviceProvider =>
                    new FopDocumentGenerationEngine(
                        serviceProvider.GetRequiredService<IHttpClientFactory>()
                            .CreateClient(FopDocumentGenerationEngine.HttpClientName)));
            }
            else
            {
                services.AddSingleton(engineType);
            }

            services.AddSingleton(typeof(IDocumentGenerationEngine), serviceProvider =>
                (IDocumentGenerationEngine)serviceProvider.GetRequiredService(engineType));
        }

        return services;
    }

    private static IServiceCollection AddDocumentProcessingResilience(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GenerationResilienceOptions>(
            configuration.GetSection(GenerationResilienceOptions.SectionName));

        var fopOptions = configuration
            .GetSection(DocumentProcessingFopOptions.SectionName)
            .Get<DocumentProcessingFopOptions>()
            ?? new DocumentProcessingFopOptions();

        if (!Uri.TryCreate(fopOptions.BaseUrl, UriKind.Absolute, out var fopBaseUri))
        {
            throw new InvalidOperationException($"{DocumentProcessingFopOptions.SectionName}:BaseUrl must be an absolute URI.");
        }

        var attemptTimeout = TimeSpan.FromSeconds(fopOptions.AttemptTimeoutSeconds);

        // HttpStandardResilienceOptions validates two invariants when the pipeline is built: the
        // breaker sampling window must cover at least two attempt timeouts, and the total request
        // timeout must cover every attempt. Widening them here keeps a hand-edited configuration
        // from turning the first FOP request into an OptionsValidationException.
        var samplingDuration = TimeSpan.FromSeconds(
            Math.Max(fopOptions.CircuitBreakerSamplingDurationSeconds, 2 * fopOptions.AttemptTimeoutSeconds));
        var totalTimeout = TimeSpan.FromSeconds(
            Math.Max(fopOptions.TotalTimeoutSeconds, fopOptions.AttemptTimeoutSeconds * (fopOptions.MaxRetryAttempts + 1)));

        // The resilience handler owns request timeouts and sets HttpClient.Timeout to infinite itself,
        // so no client-level timeout is configured here.
        services.AddHttpClient(FopDocumentGenerationEngine.HttpClientName, client => client.BaseAddress = fopBaseUri)
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = attemptTimeout;
                options.TotalRequestTimeout.Timeout = totalTimeout;
                options.Retry.MaxRetryAttempts = fopOptions.MaxRetryAttempts;
                options.Retry.Delay = TimeSpan.FromMilliseconds(fopOptions.RetryBaseDelayMilliseconds);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.CircuitBreaker.MinimumThroughput = fopOptions.CircuitBreakerMinimumThroughput;
                options.CircuitBreaker.SamplingDuration = samplingDuration;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(fopOptions.CircuitBreakerBreakDurationSeconds);
            });

        return services;
    }

    /// <summary>
    /// Registers the named external API targets the <c>api</c> engine can call. Targets are
    /// configuration-only, so a request may choose a target by name but can never supply a URL or a
    /// credential, which is what keeps the engine from becoming an open proxy.
    /// </summary>
    private static IServiceCollection AddDocumentProcessingApiTargets(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DocumentProcessingApiOptions>(
            configuration.GetSection(DocumentProcessingApiOptions.SectionName));

        var apiOptions = configuration
            .GetSection(DocumentProcessingApiOptions.SectionName)
            .Get<DocumentProcessingApiOptions>()
            ?? new DocumentProcessingApiOptions();

        // Fail at startup rather than on the first request, matching the FOP base URL check.
        apiOptions.Validate();

        // No resilience handler here on purpose: a configured target is an unknown third party whose
        // endpoints may not be idempotent, so an automatic retry could duplicate a side effect. Each
        // target still gets its own TimeoutSeconds deadline through a linked cancellation token.
        services.AddHttpClient(ApiDocumentGenerationEngine.HttpClientName);
        services.AddHttpClient(ApiAuthTokenProvider.TokenClientName);

        services.TryAddSingleton<IApiAuthTokenProvider, ApiAuthTokenProvider>();

        return services;
    }

    private static IServiceCollection AddDocumentProcessingCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DocumentProcessingCacheOptions>(
            configuration.GetSection(DocumentProcessingCacheOptions.SectionName));
        services.Configure<Application.Services.InMemoryProviderOptions>(
            configuration.GetSection(Application.Services.InMemoryProviderOptions.SectionName));

        var cacheOptions = configuration
            .GetSection(DocumentProcessingCacheOptions.SectionName)
            .Get<DocumentProcessingCacheOptions>()
            ?? new DocumentProcessingCacheOptions();

        if (!cacheOptions.IsRedisProvider())
        {
            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var redisOptions = ConfigurationOptions.Parse(cacheOptions.Redis.ConnectionString);
            redisOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(redisOptions);
        });

        services.Replace(ServiceDescriptor.Singleton<ICompiledTemplateCache, RedisCompiledTemplateCache>());
        services.Replace(ServiceDescriptor.Singleton<IGeneratedDocumentCache, RedisGeneratedDocumentCache>());
        services.Replace(ServiceDescriptor.Singleton<IGenerationRequestDeduplicationStore, RedisGenerationRequestDeduplicationStore>());

        // RedisTimeoutException already derives from TimeoutException, so only the connection fault
        // needs registering for the application layer to treat it as retryable.
        services.Replace(ServiceDescriptor.Singleton(
            typeof(TransientFailureClassifier),
            new TransientFailureClassifier(typeof(RedisConnectionException))));

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

                    // The scheduler sends straight to queue:<QueueName>, so the consumer has to
                    // listen on that endpoint instead of the convention-derived message name.
                    cfg.ReceiveEndpoint(messagingOptions.QueueName, endpoint =>
                    {
                        endpoint.ConfigureConsumer<GenerateDocumentRequestedConsumer>(context);
                    });
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

        services.AddScoped<ArtifactRetentionCleaner>();

        if (!persistenceOptions.IsPostgreSqlProvider())
        {
            services.AddHostedService<ArtifactCleanupBackgroundService>();
            return services;
        }

        services.AddDbContext<DocumentGenerationDbContext>(options =>
            options.UseNpgsql(persistenceOptions.PostgreSql.ConnectionString));

        services.AddScoped<
            Application.Abstractions.Persistence.IDocumentGenerationTaskRepository,
            PostgreSqlDocumentGenerationTaskRepository>();
        services.Replace(ServiceDescriptor.Scoped<
            IAsyncDocumentGenerationSubmission,
            PostgreSqlAsyncDocumentGenerationSubmission>());

        services.AddHostedService<ArtifactCleanupBackgroundService>();
        services.AddHostedService<PostgreSqlDocumentGenerationOutboxDispatcher>();

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
            healthChecks.AddCheck<PostgreSqlConnectionHealthCheck>(
                "postgresql-persistence",
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
            healthChecks.AddCheck<MinioBucketHealthCheck>(
                "object-storage",
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
