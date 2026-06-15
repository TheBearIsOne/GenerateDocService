using System.Text;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;

namespace GenerateDocService.Api.Validation;

public sealed class GenerateDocumentRequestValidator(DocumentProcessingValidationOptions options)
{
    public ValidationResult Validate(GenerateDocumentHttpRequest request)
    {
        var errors = new List<ValidationError>();

        if (request is null)
        {
            return new ValidationResult([new ValidationError("body", "Request body is required.")]);
        }

        ValidateRequiredFields(request, errors);
        ValidatePayloadSize(request, errors);
        ValidateTemplateSize(request, errors);
        ValidateMetadata(request, errors);
        ValidateEngineName(request, errors);
        ValidateFormatNames(request, errors);
        ValidateRequestId(request, errors);

        return new ValidationResult(errors);
    }

    private void ValidateRequiredFields(GenerateDocumentHttpRequest request, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(request.InputFormat))
        {
            errors.Add(new ValidationError("inputFormat", "Input format is required."));
        }

        if (string.IsNullOrWhiteSpace(request.OutputFormat))
        {
            errors.Add(new ValidationError("outputFormat", "Output format is required."));
        }

        if (string.IsNullOrEmpty(request.Payload))
        {
            errors.Add(new ValidationError("payload", "Payload is required."));
        }
    }

    private void ValidatePayloadSize(GenerateDocumentHttpRequest request, List<ValidationError> errors)
    {
        if (!string.IsNullOrEmpty(request.Payload))
        {
            var payloadSize = Encoding.UTF8.GetByteCount(request.Payload);
            if (payloadSize > options.MaxPayloadSizeBytes)
            {
                errors.Add(new ValidationError(
                    "payload",
                    $"Payload size ({FormatBytes(payloadSize)}) exceeds maximum allowed size ({FormatBytes(options.MaxPayloadSizeBytes)})."));
            }
        }
    }

    private void ValidateTemplateSize(GenerateDocumentHttpRequest request, List<ValidationError> errors)
    {
        if (!string.IsNullOrEmpty(request.Template))
        {
            var templateSize = Encoding.UTF8.GetByteCount(request.Template);
            if (templateSize > options.MaxTemplateSizeBytes)
            {
                errors.Add(new ValidationError(
                    "template",
                    $"Template size ({FormatBytes(templateSize)}) exceeds maximum allowed size ({FormatBytes(options.MaxTemplateSizeBytes)})."));
            }
        }
    }

    private void ValidateMetadata(GenerateDocumentHttpRequest request, List<ValidationError> errors)
    {
        if (request.Metadata is null)
        {
            return;
        }

        if (request.Metadata.Count > options.MaxMetadataEntries)
        {
            errors.Add(new ValidationError(
                "metadata",
                $"Metadata contains {request.Metadata.Count} entries, maximum allowed is {options.MaxMetadataEntries}."));
            return;
        }

        foreach (var (key, value) in request.Metadata)
        {
            if (string.IsNullOrEmpty(key))
            {
                errors.Add(new ValidationError("metadata", "Metadata keys must not be empty."));
                continue;
            }

            if (key.Length > options.MaxMetadataKeyLength)
            {
                errors.Add(new ValidationError(
                    "metadata",
                    $"Metadata key '{Truncate(key, 20)}' exceeds maximum length ({options.MaxMetadataKeyLength})."));
            }

            if (value is not null && value.Length > options.MaxMetadataValueLength)
            {
                errors.Add(new ValidationError(
                    "metadata",
                    $"Metadata value for key '{Truncate(key, 20)}' exceeds maximum length ({options.MaxMetadataValueLength})."));
            }
        }
    }

    private void ValidateEngineName(GenerateDocumentHttpRequest request, List<ValidationError> errors)
    {
        if (!string.IsNullOrEmpty(request.Engine) && request.Engine.Length > options.MaxEngineNameLength)
        {
            errors.Add(new ValidationError(
                "engine",
                $"Engine name exceeds maximum length ({options.MaxEngineNameLength})."));
        }
    }

    private void ValidateFormatNames(GenerateDocumentHttpRequest request, List<ValidationError> errors)
    {
        if (request.InputFormat.Length > options.MaxFormatNameLength)
        {
            errors.Add(new ValidationError(
                "inputFormat",
                $"Input format name exceeds maximum length ({options.MaxFormatNameLength})."));
        }

        if (request.OutputFormat.Length > options.MaxFormatNameLength)
        {
            errors.Add(new ValidationError(
                "outputFormat",
                $"Output format name exceeds maximum length ({options.MaxFormatNameLength})."));
        }

        if (!string.IsNullOrEmpty(request.TemplateFormat) && request.TemplateFormat.Length > options.MaxFormatNameLength)
        {
            errors.Add(new ValidationError(
                "templateFormat",
                $"Template format name exceeds maximum length ({options.MaxFormatNameLength})."));
        }
    }

    private void ValidateRequestId(GenerateDocumentHttpRequest request, List<ValidationError> errors)
    {
        if (!string.IsNullOrEmpty(request.RequestId) && request.RequestId.Length > options.MaxRequestIdLength)
        {
            errors.Add(new ValidationError(
                "requestId",
                $"Request ID exceeds maximum length ({options.MaxRequestIdLength})."));
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_048_576)
        {
            return $"{bytes / 1_048_576.0:F1} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes} B";
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

public sealed record ValidationResult(IReadOnlyList<ValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed record ValidationError(string Field, string Message);
