namespace GenerateDocService.Api.Correlation;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context);

        context.Items[CorrelationConstants.MetadataKey] = correlationId;
        context.Response.Headers[CorrelationConstants.HeaderName] = correlationId;

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationConstants.HeaderName, out var existingValues))
        {
            var existing = existingValues.ToString();
            if (!string.IsNullOrWhiteSpace(existing))
            {
                return existing;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
