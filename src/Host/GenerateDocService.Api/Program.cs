using System.Text;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.DocumentProcessing.Application.Services;
using GenerateDocService.DocumentProcessing.Infrastructure.DependencyInjection;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDocumentProcessingInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
    .WithName("GetHealth");

app.MapPost("/api/v1/documents/sync", async (
    GenerateDocumentHttpRequest request,
    SyncDocumentGenerationService service,
    CancellationToken cancellationToken) =>
{
    var command = ToApplicationRequest(request);
    using var result = await service.GenerateAsync(command, cancellationToken);

    return Results.File(
        result.ToByteArray(),
        result.ContentType,
        result.FileName);
})
.WithName("GenerateDocumentSync");

app.MapPost("/api/v1/documents/async", async (
    GenerateDocumentHttpRequest request,
    AsyncDocumentGenerationService service,
    CancellationToken cancellationToken) =>
{
    var command = ToApplicationRequest(request);
    var result = await service.EnqueueAsync(command, cancellationToken);

    return Results.Accepted($"/api/v1/tasks/{result.TaskId}", DocumentResponseMapper.ToHttpResponse(result));
})
.WithName("GenerateDocumentAsync");

app.MapGet("/api/v1/tasks/{taskId}", async (
    string taskId,
    TaskStatusQueryService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.GetAsync(taskId, cancellationToken);
    return result is null
        ? Results.NotFound()
        : Results.Ok(DocumentResponseMapper.ToHttpResponse(result));
})
.WithName("GetDocumentTaskStatus");

app.Run();

static GenerateDocumentRequest ToApplicationRequest(GenerateDocumentHttpRequest request)
{
    return new GenerateDocumentRequest(
        request.RequestId ?? Guid.NewGuid().ToString("N"),
        request.Engine,
        request.InputFormat,
        request.OutputFormat,
        request.TemplateFormat,
        Encoding.UTF8.GetBytes(request.Payload),
        request.Template is null ? null : Encoding.UTF8.GetBytes(request.Template),
        request.Metadata);
}
