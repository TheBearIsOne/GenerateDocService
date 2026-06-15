namespace GenerateDocService.Api.Security;

public static class AuthorizationPolicies
{
    public const string DocumentSubmit = nameof(DocumentSubmit);
    public const string DocumentRead = nameof(DocumentRead);
    public const string DocumentDownload = nameof(DocumentDownload);
    public const string DocumentAdmin = nameof(DocumentAdmin);

    public static string[] AllRoles => [DocumentSubmit, DocumentRead, DocumentDownload, DocumentAdmin];
}
