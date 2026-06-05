using System.Security.Cryptography;
using System.Text;
using GenerateDocService.DocumentProcessing.Application.Models;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public static class DocumentRequestFingerprintProvider
{
    public const string MetadataKey = "requestFingerprint";

    public static string Create(GenerateDocumentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var builder = new StringBuilder()
            .Append(request.Engine).Append('|')
            .Append(request.InputFormat).Append('|')
            .Append(request.OutputFormat).Append('|')
            .Append(request.TemplateFormat).Append('|')
            .Append(Convert.ToHexString(SHA256.HashData(request.Payload.Span))).Append('|');

        if (request.Template.HasValue)
        {
            builder.Append(Convert.ToHexString(SHA256.HashData(request.Template.Value.Span)));
        }

        foreach (var pair in request.Metadata
                     .Where(static pair => !string.Equals(pair.Key, "correlationId", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|').Append(pair.Key).Append('=').Append(pair.Value);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }
}
