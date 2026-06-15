namespace GenerateDocService.Api.Security;

public sealed class AuthenticationOptions
{
    public const string SectionName = "DocumentProcessing:Authentication";

    public bool Enabled { get; set; } = false;
    public string Issuer { get; set; } = "GenerateDocService";
    public string Audience { get; set; } = "GenerateDocService.Api";
    public string SigningKey { get; set; } = "change-me-to-a-secure-key-at-least-32-chars!";
}
