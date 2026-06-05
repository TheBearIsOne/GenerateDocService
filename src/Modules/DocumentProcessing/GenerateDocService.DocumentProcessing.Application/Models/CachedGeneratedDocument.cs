namespace GenerateDocService.DocumentProcessing.Application.Models;

public sealed record CachedGeneratedDocument(
    string RequestId,
    string FileName,
    string ContentType,
    string OutputFormat,
    long ContentLength,
    string? Checksum,
    IReadOnlyDictionary<string, string> Metadata,
    DocumentArtifactReference Artifact)
{
    public static CachedGeneratedDocument FromResult(GeneratedDocumentResult result, DocumentArtifactReference artifact)
        => new(
            result.RequestId,
            result.FileName,
            result.ContentType,
            result.OutputFormat,
            result.ContentLength,
            result.Checksum,
            result.Metadata,
            artifact);
}
