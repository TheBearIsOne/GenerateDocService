namespace GenerateDocService.Engine.Abstractions;

public interface IReportResult
{
    string RequestId { get; }
    string FileName { get; }
    string ContentType { get; }
    string OutputFormat { get; }
    long ContentLength { get; }
    Stream Content { get; }
    string? Checksum { get; }
    IReadOnlyDictionary<string, string> Metadata { get; }
}
