namespace GenerateDocService.Engine.Abstractions;

public interface IDocumentGenerationEngine
{
    string Name { get; }

    bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null);

    Task<IReportResult> GenerateAsync(
        IReportRequest request,
        CancellationToken cancellationToken = default);
}
