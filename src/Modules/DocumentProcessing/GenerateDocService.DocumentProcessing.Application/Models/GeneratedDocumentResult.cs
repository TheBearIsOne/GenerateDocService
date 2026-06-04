using System.Security.Cryptography;
using GenerateDocService.Engine.Abstractions;

namespace GenerateDocService.DocumentProcessing.Application.Models;

public sealed class GeneratedDocumentResult : IReportResult, IDisposable
{
    private readonly MemoryStream _content;

    public GeneratedDocumentResult(string requestId, string fileName, string contentType, string outputFormat, byte[] content, IReadOnlyDictionary<string, string>? metadata = null)
    {
        RequestId = requestId;
        FileName = fileName;
        ContentType = contentType;
        OutputFormat = outputFormat;
        _content = new MemoryStream(content, writable: false);
        ContentLength = content.LongLength;
        Checksum = Convert.ToHexString(SHA256.HashData(content));
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string RequestId { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public string OutputFormat { get; }
    public long ContentLength { get; }
    public Stream Content => _content;
    public string? Checksum { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public byte[] ToByteArray() => _content.ToArray();

    public void Dispose()
    {
        _content.Dispose();
    }
}
