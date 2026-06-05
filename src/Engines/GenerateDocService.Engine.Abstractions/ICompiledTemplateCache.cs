namespace GenerateDocService.Engine.Abstractions;

public interface ICompiledTemplateCache
{
    Task<TemplateParseResult?> GetAsync(string cacheKey, CancellationToken cancellationToken = default);

    Task SetAsync(string cacheKey, TemplateParseResult result, TimeSpan ttl, CancellationToken cancellationToken = default);
}
