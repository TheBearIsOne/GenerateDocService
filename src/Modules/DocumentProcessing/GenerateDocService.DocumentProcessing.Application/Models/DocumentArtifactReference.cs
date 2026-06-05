namespace GenerateDocService.DocumentProcessing.Application.Models;

public sealed record DocumentArtifactReference(
    string Provider,
    string StoragePath,
    string Container,
    string ObjectKey);
