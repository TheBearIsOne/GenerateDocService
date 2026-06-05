namespace GenerateDocService.DocumentProcessing.Application.Models;

public sealed class StoredDocumentArtifact
{
    private readonly byte[] _content;

    public StoredDocumentArtifact(
        string fileName,
        string contentType,
        string outputFormat,
        byte[] content,
        string? checksum,
        string storagePath,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        FileName = fileName;
        ContentType = contentType;
        OutputFormat = outputFormat;
        _content = content;
        ContentLength = content.LongLength;
        Checksum = checksum;
        StoragePath = storagePath;
        Metadata = metadata ?? new Dictionary<string, string>();
    }

    public string FileName { get; }
    public string ContentType { get; }
    public string OutputFormat { get; }
    public long ContentLength { get; }
    public string? Checksum { get; }
    public string StoragePath { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public Stream OpenRead() => new MemoryStream(_content, writable: false);

    public byte[] ToByteArray() => (byte[])_content.Clone();
}
