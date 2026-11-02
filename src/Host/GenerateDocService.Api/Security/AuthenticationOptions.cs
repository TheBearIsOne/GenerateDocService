namespace GenerateDocService.Api.Security;

public sealed class AuthenticationOptions
{
    public const string SectionName = "DocumentProcessing:Authentication";

    /// <summary>
    /// HS256 requires a signing key of at least 256 bits.
    /// </summary>
    public const int MinimumSigningKeyLength = 32;

    public bool Enabled { get; set; } = false;
    public string Issuer { get; set; } = "GenerateDocService";
    public string Audience { get; set; } = "GenerateDocService.Api";

    /// <summary>
    /// Supplied through configuration or the GDS_AUTH_SIGNING_KEY environment
    /// variable. There is deliberately no usable default: an empty value makes a
    /// missing secret fail fast instead of silently signing with a known key.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;
}
