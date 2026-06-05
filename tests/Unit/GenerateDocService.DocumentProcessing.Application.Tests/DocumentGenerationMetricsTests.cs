using GenerateDocService.DocumentProcessing.Application.Services;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class DocumentGenerationMetricsTests
{
    [Fact]
    public void Constructor_ShouldExposeExpectedMeterName()
    {
        using var metrics = new DocumentGenerationMetrics();

        DocumentGenerationMetrics.MeterName.Should().Be("GenerateDocService.DocumentProcessing");
    }

    [Fact]
    public void RecordMethods_ShouldNotThrow()
    {
        using var metrics = new DocumentGenerationMetrics();

        var action = () =>
        {
            metrics.RecordSyncRequest("scriban", "txt");
            metrics.RecordAsyncRequest("questpdf", "pdf");
            metrics.RecordCompleted("scriban", "txt", TimeSpan.FromMilliseconds(12));
            metrics.RecordFailed("questpdf", "pdf", TimeSpan.FromMilliseconds(34));
        };

        action.Should().NotThrow();
    }
}
