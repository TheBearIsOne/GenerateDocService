namespace GenerateDocService.Api.Validation;

public sealed class DocumentProcessingValidationOptions
{
    public const string SectionName = "DocumentProcessing:Validation";

    public int MaxPayloadSizeBytes { get; set; } = 10_485_760;        // 10 MB
    public int MaxTemplateSizeBytes { get; set; } = 5_242_880;       // 5 MB
    public int MaxMetadataEntries { get; set; } = 50;
    public int MaxMetadataKeyLength { get; set; } = 128;
    public int MaxMetadataValueLength { get; set; } = 1024;
    public int MaxEngineNameLength { get; set; } = 64;
    public int MaxFormatNameLength { get; set; } = 64;
    public int MaxRequestIdLength { get; set; } = 64;
    public TimeSpan MaxProcessingTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
