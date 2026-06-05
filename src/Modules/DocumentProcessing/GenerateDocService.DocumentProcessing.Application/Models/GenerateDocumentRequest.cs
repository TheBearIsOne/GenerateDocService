using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Models;

public sealed class GenerateDocumentRequest : IReportRequest
{
    public GenerateDocumentRequest(
        string requestId,
        string engine,
        string inputFormat,
        string outputFormat,
        string? templateFormat,
        byte[] payload,
        byte[]? template,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        RequestId = requestId;
        Engine = engine;
        InputFormat = inputFormat;
        OutputFormat = outputFormat;
        TemplateFormat = templateFormat;
        Payload = payload;
        Template = template;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string RequestId { get; }
    public string Engine { get; }
    public string InputFormat { get; }
    public string OutputFormat { get; }
    public string? TemplateFormat { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public ReadOnlyMemory<byte>? Template { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public GenerateDocumentRequest WithMetadata(string key, string value)
    {
        var metadata = new Dictionary<string, string>(Metadata, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };

        return new GenerateDocumentRequest(
            RequestId,
            Engine,
            InputFormat,
            OutputFormat,
            TemplateFormat,
            Payload.ToArray(),
            Template?.ToArray(),
            metadata);
    }

    public GenerateDocumentRequest WithEngine(string engine)
    {
        return new GenerateDocumentRequest(
            RequestId,
            engine,
            InputFormat,
            OutputFormat,
            TemplateFormat,
            Payload.ToArray(),
            Template?.ToArray(),
            Metadata);
    }
}
