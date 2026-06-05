using System.Text;
using GenerateDocService.DocumentProcessing.Application.Models;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Infrastructure.Engines;

[DocumentEngine(
    "fake",
    InputFormats = new[] { "json", "xml", "csv", "html", "markdown" },
    OutputFormats = new[] { "txt", "json", "html" },
    TemplateFormats = new[] { "scriban", "liquid", "html", "markdown" },
    Priority = 0)]
public sealed class FakeDocumentGenerationEngine : IDocumentGenerationEngine
{
    public string Name => "fake";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
        => string.Equals(outputFormat, "txt", StringComparison.OrdinalIgnoreCase)
           || string.Equals(outputFormat, "json", StringComparison.OrdinalIgnoreCase)
           || string.Equals(outputFormat, "html", StringComparison.OrdinalIgnoreCase);

    public Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        var payload = Encoding.UTF8.GetString(request.Payload.Span);
        var templateInfo = request.Template.HasValue
            ? $"template:{request.TemplateFormat ?? "unknown"}"
            : "template:none";

        var content = Encoding.UTF8.GetBytes(
            $"engine={Name};requestId={request.RequestId};input={request.InputFormat};output={request.OutputFormat};{templateInfo};payload={payload}");

        IReportResult result = new GeneratedDocumentResult(
            request.RequestId,
            $"{request.RequestId}.{request.OutputFormat}",
            ResolveContentType(request.OutputFormat),
            request.OutputFormat,
            content,
            request.Metadata);

        return Task.FromResult(result);
    }

    private static string ResolveContentType(string outputFormat)
        => outputFormat.ToLowerInvariant() switch
        {
            "html" => "text/html",
            "json" => "application/json",
            _ => "text/plain"
        };
}
