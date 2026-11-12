using System.Net.Sockets;
using GenerateDocService.DocumentProcessing.Application.Resilience;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class TransientFailureClassifierTests
{
    private readonly TransientFailureClassifier _classifier = new();

    [Theory]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(SocketException))]
    public void IsTransient_ShouldReturnTrue_ForKnownNetworkFailures(Type exceptionType)
    {
        _classifier.IsTransient(CreateException(exceptionType)).Should().BeTrue();
    }

    // SocketException has no string-only constructor, unlike the other network faults.
    private static Exception CreateException(Type type)
        => type == typeof(SocketException)
            ? new SocketException()
            : (Exception)Activator.CreateInstance(type, "boom")!;

    [Fact]
    public void IsTransient_ShouldReturnTrue_ForTransientInnerException()
    {
        var exception = new InvalidOperationException("wrapper", new HttpRequestException("connection refused"));

        _classifier.IsTransient(exception).Should().BeTrue();
    }

    [Fact]
    public void IsTransient_ShouldReturnTrue_ForRegisteredAdditionalType()
    {
        var classifier = new TransientFailureClassifier(typeof(CustomTransientException));

        classifier.IsTransient(new CustomTransientException()).Should().BeTrue();
        _classifier.IsTransient(new CustomTransientException()).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ShouldReturnFalse_ForApplicationFailures()
    {
        _classifier.IsTransient(new InvalidOperationException("engine cannot handle format")).Should().BeFalse();
        _classifier.IsTransient(new ArgumentException("bad payload")).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ShouldReturnFalse_ForCancellation_EvenWhenWrappingTransientFault()
    {
        _classifier.IsTransient(new OperationCanceledException()).Should().BeFalse();
        _classifier.IsTransient(new TaskCanceledException()).Should().BeFalse();

        var cancelled = new InvalidOperationException("wrapper", new TaskCanceledException("timed out", new HttpRequestException("inner")));
        _classifier.IsTransient(cancelled).Should().BeFalse();
    }

    [Fact]
    public void IsTransient_ShouldReturnFalse_WhenTheEngineOptedOutOfRetries()
    {
        // The api engine wraps upstream failures this way, because the request may already have
        // reached a third party whose endpoint is not idempotent.
        var wrapped = new GenerateDocService.Engine.Abstractions.NonRetryableGenerationException(
            "API target 'x' failed",
            new HttpRequestException("connection reset"));

        _classifier.IsTransient(wrapped).Should().BeFalse();
    }

    [Fact]
    public void Constructor_ShouldRejectNonExceptionType()
    {
        var act = () => new TransientFailureClassifier(typeof(string));

        act.Should().Throw<ArgumentException>();
    }

    private sealed class CustomTransientException : Exception
    {
    }
}
