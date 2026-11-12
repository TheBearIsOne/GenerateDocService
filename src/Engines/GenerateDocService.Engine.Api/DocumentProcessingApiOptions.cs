namespace GenerateDocService.Engine.Api;

public enum ApiAuthenticationScheme
{
    None = 0,
    Basic = 1,
    Bearer = 2,
    OAuth2ClientCredentials = 3
}

public enum ApiHttpMethod
{
    Post = 0,
    Put = 1
}

/// <summary>
/// Named external API targets the <c>api</c> engine can call. Targets live in configuration rather
/// than in the request, so credentials never travel in a request body and the engine can only ever
/// reach a pre-configured host, which removes the SSRF surface a request-supplied URL would create.
/// </summary>
public sealed class DocumentProcessingApiOptions
{
    public const string SectionName = "DocumentProcessing:Api";

    /// <summary>Metadata key a request uses to pick a target by name.</summary>
    public const string TargetMetadataKey = "api.target";

    public Dictionary<string, ApiTargetOptions> Targets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Target used when a request does not name one. Must be a key in <see cref="Targets"/>.</summary>
    public string? DefaultTarget { get; set; }

    /// <summary>
    /// Fails fast on a misconfigured target so a typo surfaces at startup instead of as a 500 on
    /// the first request. Mirrors the eager check the FOP base URL already gets.
    /// </summary>
    public void Validate()
    {
        foreach (var (name, target) in Targets)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException($"{SectionName}:Targets contains an empty target name.");
            }

            if (!Uri.TryCreate(target.BaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    $"{SectionName}:Targets:{name}:BaseUrl must be an absolute URI, but was '{target.BaseUrl}'.");
            }

            if (string.IsNullOrWhiteSpace(target.RequestPath))
            {
                throw new InvalidOperationException($"{SectionName}:Targets:{name}:RequestPath is required.");
            }

            if (target.TimeoutSeconds < 0)
            {
                throw new InvalidOperationException(
                    $"{SectionName}:Targets:{name}:TimeoutSeconds must be zero or greater; zero means no engine-imposed deadline.");
            }

            ValidateAuthentication(name, target.Authentication);
        }

        if (!string.IsNullOrWhiteSpace(DefaultTarget) && !Targets.ContainsKey(DefaultTarget))
        {
            throw new InvalidOperationException(
                $"{SectionName}:DefaultTarget '{DefaultTarget}' is not one of the configured targets: {string.Join(", ", Targets.Keys)}.");
        }
    }

    private static void ValidateAuthentication(string targetName, ApiAuthenticationOptions authentication)
    {
        var prefix = $"{SectionName}:Targets:{targetName}:Authentication";

        switch (authentication.Scheme)
        {
            case ApiAuthenticationScheme.None:
                return;

            case ApiAuthenticationScheme.Basic:
                if (string.IsNullOrWhiteSpace(authentication.Username) || authentication.Password is null)
                {
                    throw new InvalidOperationException($"{prefix}: Basic requires both Username and Password.");
                }

                return;

            case ApiAuthenticationScheme.Bearer:
                if (string.IsNullOrWhiteSpace(authentication.Token))
                {
                    throw new InvalidOperationException($"{prefix}: Bearer requires Token.");
                }

                return;

            case ApiAuthenticationScheme.OAuth2ClientCredentials:
                if (!Uri.TryCreate(authentication.TokenUrl, UriKind.Absolute, out _))
                {
                    throw new InvalidOperationException($"{prefix}: OAuth2ClientCredentials requires an absolute TokenUrl.");
                }

                if (string.IsNullOrWhiteSpace(authentication.ClientId) || string.IsNullOrWhiteSpace(authentication.ClientSecret))
                {
                    throw new InvalidOperationException($"{prefix}: OAuth2ClientCredentials requires ClientId and ClientSecret.");
                }

                return;

            default:
                throw new InvalidOperationException($"{prefix}: unknown scheme '{authentication.Scheme}'.");
        }
    }
}

public sealed class ApiTargetOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path and query appended to <see cref="BaseUrl"/>. The tokens <c>{requestId}</c> and
    /// <c>{correlationId}</c> are substituted per request, which is what the FOP converter needs.
    /// </summary>
    public string RequestPath { get; set; } = string.Empty;

    public ApiHttpMethod Method { get; set; } = ApiHttpMethod.Post;

    /// <summary>Content type sent with the payload. Defaults to binary, which suits a converter API.</summary>
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// When set, the payload is sent as a <c>multipart/form-data</c> part under this field name
    /// instead of as the raw request body. The FOP converter requires this shape.
    /// </summary>
    public string? MultipartFieldName { get; set; }

    /// <summary>Format this target consumes. Null means any, so the target matches every request.</summary>
    public string? InputFormat { get; set; }

    /// <summary>Format this target produces. Null means the response content type decides.</summary>
    public string? OutputFormat { get; set; }

    /// <summary>Per-request deadline. Zero leaves only the caller's cancellation token in force.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    public ApiAuthenticationOptions Authentication { get; set; } = new();
}

public sealed class ApiAuthenticationOptions
{
    public ApiAuthenticationScheme Scheme { get; set; } = ApiAuthenticationScheme.None;

    public string? Username { get; set; }
    public string? Password { get; set; }

    public string? Token { get; set; }

    public string? TokenUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; }

    /// <summary>Refresh this many seconds before expiry so a token cannot die mid-request.</summary>
    public int TokenExpirySkewSeconds { get; set; } = 30;
}
