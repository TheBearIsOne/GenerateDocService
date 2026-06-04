namespace GenerateDocService.Engine.Abstractions;

public interface IReportRequest
{
    string RequestId { get; }
    string Engine { get; }
    string InputFormat { get; }
    string OutputFormat { get; }
    string? TemplateFormat { get; }
    ReadOnlyMemory<byte> Payload { get; }
    ReadOnlyMemory<byte>? Template { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
}
