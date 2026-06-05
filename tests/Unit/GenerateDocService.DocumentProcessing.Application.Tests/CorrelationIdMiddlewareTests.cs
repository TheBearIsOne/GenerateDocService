using GenerateDocService.Api.Correlation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldReuseIncomingCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationConstants.HeaderName] = "corr-123";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers[CorrelationConstants.HeaderName].ToString().Should().Be("corr-123");
        context.Items[CorrelationConstants.MetadataKey].Should().Be("corr-123");
    }

    [Fact]
    public async Task InvokeAsync_ShouldGenerateCorrelationId_WhenHeaderIsMissing()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var headerValue = context.Response.Headers[CorrelationConstants.HeaderName].ToString();
        headerValue.Should().NotBeNullOrWhiteSpace();
        context.Items[CorrelationConstants.MetadataKey].Should().Be(headerValue);
    }
}
