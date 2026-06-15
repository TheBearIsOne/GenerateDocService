using System.Text;
using GenerateDocService.Api.Correlation;
using GenerateDocService.Api.Security;
using GenerateDocService.Api.Validation;
using GenerateDocService.DocumentProcessing.Application.Abstractions.Engines;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "GenerateDocService.Api")
        .WriteTo.Console();
});

builder.Services.AddOpenApi();
builder.Services.AddDocumentProcessingInfrastructure(builder.Configuration);

var authOptions = builder.Configuration
    .GetSection(AuthenticationOptions.SectionName)
    .Get<AuthenticationOptions>()
    ?? new AuthenticationOptions();

builder.Services.AddDocumentAuthentication(builder.Configuration, authOptions);

builder.Services.Configure<DocumentProcessingValidationOptions>(
    builder.Configuration.GetSection(DocumentProcessingValidationOptions.SectionName));
builder.Services.AddSingleton<GenerateDocumentRequestValidator>(sp =>
{
    var options = sp.GetRequiredService<IOptions<DocumentProcessingValidationOptions>>().Value;
    return new GenerateDocumentRequestValidator(options);
});
builder.Services.AddTransient<ValidationRequestFilter>();
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("GenerateDocService.Api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options => options.RecordException = true)
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
            .AddMeter(DocumentGenerationMetrics.MeterName)
            .AddConsoleExporter();
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

if (authOptions.Enabled)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
})
    .WithName("GetLiveHealth")
    .WithTags("Health");

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
})
    .WithName("GetReadyHealth")
    .WithTags("Health");

app.MapGet("/api/v1/engines", (IDocumentGenerationEngineRegistry registry) =>
{
    var response = registry.GetDescriptors()
        .OrderByDescending(descriptor => descriptor.Priority)
        .ThenBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
        .Select(DocumentResponseMapper.ToHttpResponse)
        .ToArray();

    return Results.Ok(response);
})
.WithName("GetDocumentEngines")
.WithTags("Engines")
.WithSummary("List registered document generation engines.")
.WithDescription("Returns engine metadata including supported formats, template formats, priority, and implementation type.")
.Produces<DocumentEngineHttpResponse[]>(StatusCodes.Status200OK)
    .MaybeRequireAuth(AuthorizationPolicies.DocumentRead, authOptions.Enabled);

app.MapGet("/api/v1/engines/{name}", (string name, IDocumentGenerationEngineRegistry registry) =>
{
    var descriptor = registry.FindDescriptor(name);

    return descriptor is null
        ? Results.NotFound()
        : Results.Ok(DocumentResponseMapper.ToHttpResponse(descriptor));
})
.WithName("GetDocumentEngineByName")
.WithTags("Engines")
.WithSummary("Get a registered document generation engine by name.")
.WithDescription("Returns metadata for a specific engine if it is registered.")
.Produces<DocumentEngineHttpResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
    .MaybeRequireAuth(AuthorizationPolicies.DocumentRead, authOptions.Enabled);

app.MapPost("/api/v1/documents/sync", async (
    GenerateDocumentHttpRequest request,
    SyncDocumentGenerationService service,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var command = ToApplicationRequest(request, httpContext);
    using var result = await service.GenerateAsync(command, cancellationToken);

    return Results.File(
        result.ToByteArray(),
        result.ContentType,
        result.FileName);
})
.AddEndpointFilter<ValidationRequestFilter>()
.WithName("GenerateDocumentSync")
.WithTags("Documents")
.WithSummary("Generate a document synchronously.")
.WithDescription("Processes the request immediately and returns the generated file in the HTTP response.")
.Accepts<GenerateDocumentHttpRequest>("application/json")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status500InternalServerError)
    .MaybeRequireAuth(AuthorizationPolicies.DocumentSubmit, authOptions.Enabled);

app.MapPost("/api/v1/documents/async", async (
    GenerateDocumentHttpRequest request,
    AsyncDocumentGenerationService service,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var command = ToApplicationRequest(request, httpContext);
    var result = await service.EnqueueAsync(command, cancellationToken);

    return Results.Accepted($"/api/v1/tasks/{result.TaskId}", DocumentResponseMapper.ToHttpResponse(result, BuildDownloadUrl(httpContext, result)));
})
.AddEndpointFilter<ValidationRequestFilter>()
.WithName("GenerateDocumentAsync")
.WithTags("Documents")
.WithSummary("Queue a document generation request for asynchronous processing.")
.WithDescription("Creates a generation task, returns task metadata, and exposes a status endpoint for polling.")
.Accepts<GenerateDocumentHttpRequest>("application/json")
.Produces<TaskStatusHttpResponse>(StatusCodes.Status202Accepted)
.Produces(StatusCodes.Status400BadRequest)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status500InternalServerError)
    .MaybeRequireAuth(AuthorizationPolicies.DocumentSubmit, authOptions.Enabled);

app.MapGet("/api/v1/tasks/{taskId}", async (
    string taskId,
    TaskStatusQueryService service,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    var result = await service.GetAsync(taskId, cancellationToken);
    return result is null
        ? Results.NotFound()
        : Results.Ok(DocumentResponseMapper.ToHttpResponse(result, BuildDownloadUrl(httpContext, result)));
})
.WithName("GetDocumentTaskStatus")
.WithTags("Tasks")
.WithSummary("Get the status of an asynchronous document generation task.")
.WithDescription("Returns queued, processing, completed, or failed state along with download information when available.")
.Produces<TaskStatusHttpResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
    .MaybeRequireAuth(AuthorizationPolicies.DocumentRead, authOptions.Enabled);

app.MapGet("/api/v1/tasks/{taskId}/download", async (
    string taskId,
    DocumentArtifactDownloadService service,
    CancellationToken cancellationToken) =>
{
    var artifact = await service.GetAsync(taskId, cancellationToken);
    if (artifact is null)
    {
        return Results.NotFound();
    }

    return Results.File(
        artifact.ToByteArray(),
        artifact.ContentType,
        artifact.FileName);
})
.WithName("DownloadGeneratedDocument")
.WithTags("Tasks")
.WithSummary("Download the generated artifact for a completed task.")
.WithDescription("Returns the generated file when the asynchronous task has completed and the artifact is available.")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound)
    .MaybeRequireAuth(AuthorizationPolicies.DocumentDownload, authOptions.Enabled);

app.Run();

static GenerateDocumentRequest ToApplicationRequest(GenerateDocumentHttpRequest request, HttpContext httpContext)
{
    var metadata = request.Metadata is null
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase);

    metadata[CorrelationConstants.MetadataKey] = httpContext.Items[CorrelationConstants.MetadataKey]?.ToString()
        ?? Guid.NewGuid().ToString("N");

    return new GenerateDocumentRequest(
        request.RequestId ?? Guid.NewGuid().ToString("N"),
        request.Engine ?? string.Empty,
        request.InputFormat,
        request.OutputFormat,
        request.TemplateFormat,
        Encoding.UTF8.GetBytes(request.Payload),
        request.Template is null ? null : Encoding.UTF8.GetBytes(request.Template),
        metadata);
}

static string? BuildDownloadUrl(HttpContext httpContext, TaskStatusResponse response)
{
    if (response.Status != GenerateDocService.DocumentProcessing.Domain.Tasks.GenerationTaskStatus.Completed
        || string.IsNullOrWhiteSpace(response.ResultStoragePath))
    {
        return null;
    }

    return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/v1/tasks/{response.TaskId}/download";
}
