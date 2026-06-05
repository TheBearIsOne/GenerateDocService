using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GenerateDocService.DocumentProcessing.Application.Services;

public sealed class DocumentGenerationMetrics : IDisposable
{
    public const string MeterName = "GenerateDocService.DocumentProcessing";

    private readonly Meter _meter = new(MeterName, "1.0.0");
    private readonly Counter<long> _syncRequestCounter;
    private readonly Counter<long> _asyncRequestCounter;
    private readonly Counter<long> _completedGenerationCounter;
    private readonly Counter<long> _failedGenerationCounter;
    private readonly Histogram<double> _generationDurationMs;

    public DocumentGenerationMetrics()
    {
        _syncRequestCounter = _meter.CreateCounter<long>("document_generation_sync_requests_total");
        _asyncRequestCounter = _meter.CreateCounter<long>("document_generation_async_requests_total");
        _completedGenerationCounter = _meter.CreateCounter<long>("document_generation_completed_total");
        _failedGenerationCounter = _meter.CreateCounter<long>("document_generation_failed_total");
        _generationDurationMs = _meter.CreateHistogram<double>("document_generation_duration_ms", unit: "ms");
    }

    public void RecordSyncRequest(string engine, string outputFormat)
        => _syncRequestCounter.Add(1, CreateTags(engine, outputFormat));

    public void RecordAsyncRequest(string engine, string outputFormat)
        => _asyncRequestCounter.Add(1, CreateTags(engine, outputFormat));

    public void RecordCompleted(string engine, string outputFormat, TimeSpan duration)
    {
        var tags = CreateTags(engine, outputFormat);
        _completedGenerationCounter.Add(1, tags);
        _generationDurationMs.Record(duration.TotalMilliseconds, tags);
    }

    public void RecordFailed(string engine, string outputFormat, TimeSpan duration)
    {
        var tags = CreateTags(engine, outputFormat);
        _failedGenerationCounter.Add(1, tags);
        _generationDurationMs.Record(duration.TotalMilliseconds, tags);
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    private static TagList CreateTags(string engine, string outputFormat)
    {
        var tags = new TagList
        {
            { "engine", engine },
            { "output_format", outputFormat }
        };

        return tags;
    }
}
