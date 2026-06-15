using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

var loggerConfiguration = new Serilog.LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "GenerateDocService.Worker")
    .WriteTo.Console()
    .CreateLogger();

Log.Logger = loggerConfiguration;

builder.Services.AddDocumentProcessingInfrastructure(builder.Configuration);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("GenerateDocService.Worker"))
    .WithTracing(tracing =>
    {
        tracing
            .AddHttpClientInstrumentation(options => options.RecordException = true)
            .AddSource("MassTransit")
            .AddConsoleExporter();

        var otlpEndpoint = builder.Configuration["OpenTelemetry:Tracing:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(GenerateDocService.DocumentProcessing.Application.Services.DocumentGenerationMetrics.MeterName)
            .AddConsoleExporter();
    });

var host = builder.Build();
host.Run();
