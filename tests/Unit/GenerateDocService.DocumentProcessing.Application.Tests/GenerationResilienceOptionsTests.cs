using GenerateDocService.DocumentProcessing.Application.Resilience;
using FluentAssertions;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class GenerationResilienceOptionsTests
{
    [Fact]
    public void GetRetryDelay_ShouldBackOffExponentially_AndCapAtMaxDelay()
    {
        var options = new GenerationResilienceOptions
        {
            InitialDelayMilliseconds = 100,
            MaxDelayMilliseconds = 350,
            BackoffMultiplier = 2.0
        };

        options.GetRetryDelay(0).Should().Be(TimeSpan.FromMilliseconds(100));
        options.GetRetryDelay(1).Should().Be(TimeSpan.FromMilliseconds(200));
        options.GetRetryDelay(2).Should().Be(TimeSpan.FromMilliseconds(350));
        options.GetRetryDelay(9).Should().Be(TimeSpan.FromMilliseconds(350));
    }

    [Fact]
    public void GetRetryDelay_ShouldRejectNegativeAttempt()
    {
        var act = () => new GenerationResilienceOptions().GetRetryDelay(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetRetryDelay_ShouldTreatSubUnityMultiplierAsConstantDelay()
    {
        var options = new GenerationResilienceOptions
        {
            InitialDelayMilliseconds = 100,
            MaxDelayMilliseconds = 5_000,
            BackoffMultiplier = 0.5
        };

        options.GetRetryDelay(0).Should().Be(TimeSpan.FromMilliseconds(100));
        options.GetRetryDelay(3).Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Defaults_ShouldBoundGenerationAtFiveMinutesAndThreeAttempts()
    {
        var options = new GenerationResilienceOptions();

        options.ProcessingTimeoutSeconds.Should().Be(300);
        options.MaxAttempts.Should().Be(3);
    }
}
