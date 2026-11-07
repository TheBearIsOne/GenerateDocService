namespace GenerateDocService.Api;

/// <summary>
/// Controls surface that is useful while developing but leaks implementation detail in
/// production. Both flags are additionally forced on by the Development environment, so local
/// `dotnet run` keeps OpenAPI, Scalar, and detailed health output without any configuration.
/// </summary>
public sealed class ApiHostOptions
{
    public const string SectionName = "DocumentProcessing:Api";

    /// <summary>Serve <c>/openapi/v1.json</c> and the Scalar reference UI.</summary>
    public bool ExposeOpenApi { get; set; }

    /// <summary>
    /// Return per-check JSON from <c>/health/live</c> and <c>/health/ready</c>. When false the
    /// probes answer with the bare overall status, which keeps engine implementation class names
    /// away from anonymous callers.
    /// </summary>
    public bool ExposeHealthDetails { get; set; }
}
