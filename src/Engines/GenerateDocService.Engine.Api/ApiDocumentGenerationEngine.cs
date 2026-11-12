using System.Net.Http.Headers;
using GenerateDocService.Engine.Abstractions;
using Microsoft.Extensions.Options;

namespace GenerateDocService.Engine.Api;

/// <summary>
/// Calls a configured external HTTP API and returns its response as the generated document. The
/// target and its credentials come from configuration, never from the request, so a caller can pick
/// a pre-approved target but cannot point the service at an arbitrary host.
/// </summary>
/// <remarks>
/// Deliberately has no HTTP-layer retry. Unlike the FOP adapter, a configured target is an unknown
/// third party whose endpoints may not be idempotent, so automatic retries could duplicate a side
/// effect. Each target gets a timeout instead, and the caller decides whether to resubmit.
/// </remarks>
[DocumentEngine("api", InputFormats = [], OutputFormats = [], TemplateFormats = [], Priority = 10)]
public sealed class ApiDocumentGenerationEngine(
    IHttpClientFactory httpClientFactory,
    IOptions<DocumentProcessingApiOptions> options,
    IApiAuthTokenProvider authTokenProvider) : IDocumentGenerationEngine
{
    public const string HttpClientName = "api-engine";

    /// <summary>Metadata key naming the target. Kept in sync with the API host's correlation key.</summary>
    private const string CorrelationMetadataKey = "correlationId";

    public string Name => "api";

    public bool CanHandle(string inputFormat, string outputFormat, string? templateFormat = null)
    {
        // A generic HTTP passthrough has nothing to render a template with.
        if (!string.IsNullOrWhiteSpace(templateFormat))
        {
            return false;
        }

        return OrderedTargets().Any(pair => Matches(pair.Value, inputFormat, outputFormat));
    }

    public async Task<IReportResult> GenerateAsync(IReportRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!CanHandle(request.InputFormat, request.OutputFormat, request.TemplateFormat))
        {
            throw new InvalidOperationException(
                $"Engine '{Name}' cannot handle {request.InputFormat} -> {request.OutputFormat} with template '{request.TemplateFormat}'.");
        }

        var (targetName, target) = ResolveTarget(request);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (target.TimeoutSeconds > 0)
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(target.TimeoutSeconds));
        }

        using var content = BuildContent(target, request);

        using var message = new HttpRequestMessage(ToHttpMethod(target.Method), BuildUri(target, request))
        {
            Content = content
        };

        var authorization = await authTokenProvider.GetAsync(target.Authentication, deadline.Token);
        if (authorization is not null)
        {
            message.Headers.Authorization = authorization;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var response = await client.SendAsync(message, deadline.Token);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsByteArrayAsync(deadline.Token);
            if (body.Length == 0)
            {
                throw new InvalidOperationException($"API target '{targetName}' returned an empty response body.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? target.ContentType;
            var outputFormat = ResolveOutputFormat(target, contentType);

            return new ApiReportResult(
                request.RequestId,
                $"{request.RequestId}.{ResolveExtension(outputFormat)}",
                contentType,
                outputFormat,
                body,
                request.Metadata);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The request may already have reached the upstream, and a configured target is an
            // unknown third party whose endpoints may not be idempotent, so a retry could repeat a
            // side effect. Mark it terminal; cancellation and deadline expiry are left untouched
            // because the classifier already treats those as final.
            throw new NonRetryableGenerationException(
                $"API target '{targetName}' failed: {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// An explicitly named target must exist and be able to handle the request; otherwise the
    /// configured default wins, and failing that the first target whose declared formats match,
    /// ordered by name so resolution does not depend on dictionary enumeration order.
    /// </summary>
    private (string Name, ApiTargetOptions Target) ResolveTarget(IReportRequest request)
    {
        var configured = options.Value;

        if (request.Metadata.TryGetValue(DocumentProcessingApiOptions.TargetMetadataKey, out var requested)
            && !string.IsNullOrWhiteSpace(requested))
        {
            if (!configured.Targets.TryGetValue(requested, out var named))
            {
                throw new InvalidOperationException(
                    $"API target '{requested}' is not configured under {DocumentProcessingApiOptions.SectionName}:Targets.");
            }

            if (!Matches(named, request.InputFormat, request.OutputFormat))
            {
                throw new InvalidOperationException(
                    $"API target '{requested}' cannot handle {request.InputFormat} -> {request.OutputFormat}.");
            }

            return (requested, named);
        }

        if (!string.IsNullOrWhiteSpace(configured.DefaultTarget))
        {
            if (!configured.Targets.TryGetValue(configured.DefaultTarget, out var fallback))
            {
                throw new InvalidOperationException(
                    $"{DocumentProcessingApiOptions.SectionName}:DefaultTarget '{configured.DefaultTarget}' is not configured.");
            }

            return (configured.DefaultTarget, fallback);
        }

        foreach (var (name, target) in OrderedTargets())
        {
            if (Matches(target, request.InputFormat, request.OutputFormat))
            {
                return (name, target);
            }
        }

        throw new InvalidOperationException(
            $"No API target is configured for {request.InputFormat} -> {request.OutputFormat}. "
            + $"Set the '{DocumentProcessingApiOptions.TargetMetadataKey}' metadata key, "
            + $"or configure {DocumentProcessingApiOptions.SectionName}:DefaultTarget.");
    }

    private IEnumerable<KeyValuePair<string, ApiTargetOptions>> OrderedTargets()
        => options.Value.Targets.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    private static bool Matches(ApiTargetOptions target, string inputFormat, string outputFormat)
        => (string.IsNullOrWhiteSpace(target.InputFormat)
            || string.Equals(target.InputFormat, inputFormat, StringComparison.OrdinalIgnoreCase))
           && (string.IsNullOrWhiteSpace(target.OutputFormat)
            || string.Equals(target.OutputFormat, outputFormat, StringComparison.OrdinalIgnoreCase));

    private static Uri BuildUri(ApiTargetOptions target, IReportRequest request)
    {
        var correlationId = request.Metadata.TryGetValue(CorrelationMetadataKey, out var value)
            ? value
            : request.RequestId;

        var path = target.RequestPath
            .Replace("{requestId}", Uri.EscapeDataString(request.RequestId), StringComparison.Ordinal)
            .Replace("{correlationId}", Uri.EscapeDataString(correlationId), StringComparison.Ordinal);

        return new Uri(new Uri(target.BaseUrl), path);
    }

    /// <summary>
    /// Sends the payload as a raw body, or as a named multipart part when the target requires it.
    /// The FOP converter only accepts the multipart form, which is why this is configurable rather
    /// than fixed.
    /// </summary>
    private static HttpContent BuildContent(ApiTargetOptions target, IReportRequest request)
    {
        var part = new ByteArrayContent(request.Payload.ToArray());
        part.Headers.ContentType = new MediaTypeHeaderValue(target.ContentType);

        if (string.IsNullOrWhiteSpace(target.MultipartFieldName))
        {
            return part;
        }

        var multipart = new MultipartFormDataContent();
        multipart.Add(part, target.MultipartFieldName, $"{request.RequestId}{ResolveInputExtension(request.InputFormat)}");

        return multipart;
    }

    private static string ResolveInputExtension(string inputFormat)
        => inputFormat.ToLowerInvariant() switch
        {
            "xsl-fo" or "fo" => ".fo",
            "markdown" => ".md",
            _ => "." + inputFormat.ToLowerInvariant()
        };

    private static HttpMethod ToHttpMethod(ApiHttpMethod method)
        => method == ApiHttpMethod.Put ? HttpMethod.Put : HttpMethod.Post;

    private static string ResolveOutputFormat(ApiTargetOptions target, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(target.OutputFormat))
        {
            return target.OutputFormat;
        }

        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => "pdf",
            "application/json" => "json",
            "text/plain" => "txt",
            "text/html" => "html",
            "text/markdown" => "md",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "docx",
            _ => "bin"
        };
    }

    private static string ResolveExtension(string outputFormat)
        => outputFormat.ToLowerInvariant() switch
        {
            "markdown" => "md",
            "bin" => "dat",
            _ => outputFormat.ToLowerInvariant()
        };

    private sealed class ApiReportResult(
        string requestId,
        string fileName,
        string contentType,
        string outputFormat,
        byte[] content,
        IReadOnlyDictionary<string, string> metadata) : IReportResult, IDisposable
    {
        private readonly MemoryStream _content = new(content, writable: false);

        public string RequestId { get; } = requestId;
        public string FileName { get; } = fileName;
        public string ContentType { get; } = contentType;
        public string OutputFormat { get; } = outputFormat;
        public long ContentLength { get; } = content.LongLength;
        public Stream Content => _content;
        public string? Checksum => null;
        public IReadOnlyDictionary<string, string> Metadata { get; } = metadata;

        public void Dispose() => _content.Dispose();
    }
}
