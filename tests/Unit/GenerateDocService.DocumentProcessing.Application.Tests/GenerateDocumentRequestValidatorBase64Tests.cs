using FluentAssertions;
using GenerateDocService.Api.Validation;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class GenerateDocumentRequestValidatorBase64Tests
{
    [Fact]
    public void Validate_ShouldAcceptABinaryPayloadWithNoTextPayload()
    {
        var result = CreateValidator().Validate(BinaryRequest(payloadBase64: Convert.ToBase64String([1, 2, 3])));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldAcceptATextPayloadWithNoBinaryPayload()
    {
        var result = CreateValidator().Validate(TextRequest("""{"a":1}"""));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldRejectBothPayloadFields()
    {
        var request = TextRequest("""{"a":1}""") with { PayloadBase64 = Convert.ToBase64String([1, 2, 3]) };

        var result = CreateValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "payload" && e.Message.Contains("not both"));
    }

    [Fact]
    public void Validate_ShouldRejectBothTemplateFields()
    {
        var request = TextRequest("""{"a":1}""") with
        {
            Template = "{{ a }}",
            TemplateBase64 = Convert.ToBase64String([1, 2, 3])
        };

        var result = CreateValidator().Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "template" && e.Message.Contains("not both"));
    }

    [Fact]
    public void Validate_ShouldRejectWhenNeitherPayloadFieldIsSet()
    {
        var result = CreateValidator().Validate(TextRequest(null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "payload" && e.Message.Contains("payloadBase64"));
    }

    [Theory]
    [InlineData("not!valid!base64")]
    [InlineData("AAA")]
    [InlineData("AAAAA")]
    [InlineData("AA A")]
    [InlineData("AA==AA==")]
    [InlineData("A===")]
    [InlineData("====")]
    public void Validate_ShouldRejectMalformedBase64(string value)
    {
        var result = CreateValidator().Validate(BinaryRequest(payloadBase64: value));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "payload" && e.Message.Contains("not well-formed base64"));
    }

    [Theory]
    [InlineData("AAAA")]
    [InlineData("AAA=")]
    [InlineData("AA==")]
    [InlineData("AAAAAAAA")]
    [InlineData("YWJj+/0=")]
    public void Validate_ShouldAcceptWellFormedBase64(string value)
    {
        var result = CreateValidator().Validate(BinaryRequest(payloadBase64: value));

        result.Errors.Should().NotContain(e => e.Message.Contains("not well-formed base64"));
    }

    [Fact]
    public void Validate_ShouldMeasureBinaryPayloadSizeOnDecodedBytes()
    {
        // Three encoded bytes decode to two real bytes, so a two-byte limit must pass while a
        // one-byte limit must fail. Encoding length alone would get this the wrong way round.
        var encoded = Convert.ToBase64String([1, 2]);
        encoded.Should().EndWith("=");

        CreateValidator(maxPayloadSizeBytes: 2).Validate(BinaryRequest(encoded)).IsValid.Should().BeTrue();
        CreateValidator(maxPayloadSizeBytes: 1).Validate(BinaryRequest(encoded)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ShouldMeasureBinaryTemplateSizeOnDecodedBytes()
    {
        var encoded = Convert.ToBase64String(new byte[8]);

        var passing = CreateValidator(maxTemplateSizeBytes: 8).Validate(TextRequest("""{"a":1}""") with { TemplateBase64 = encoded });
        var failing = CreateValidator(maxTemplateSizeBytes: 7).Validate(TextRequest("""{"a":1}""") with { TemplateBase64 = encoded });

        passing.IsValid.Should().BeTrue();
        failing.IsValid.Should().BeFalse();
        failing.Errors.Should().Contain(e => e.Field == "template" && e.Message.Contains("exceeds maximum allowed size"));
    }

    [Fact]
    public void Validate_ShouldStillMeasureTextPayloadSize()
    {
        var result = CreateValidator(maxPayloadSizeBytes: 4).Validate(TextRequest("12345"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Field == "payload" && e.Message.Contains("exceeds maximum allowed size"));
    }

    private static GenerateDocumentRequestValidator CreateValidator(
        int maxPayloadSizeBytes = 10_485_760,
        int maxTemplateSizeBytes = 5_242_880)
        => new(new DocumentProcessingValidationOptions
        {
            MaxPayloadSizeBytes = maxPayloadSizeBytes,
            MaxTemplateSizeBytes = maxTemplateSizeBytes
        });

    private static GenerateDocumentHttpRequest TextRequest(string? payload)
        => new(
            RequestId: "validator-test",
            Engine: "fake",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: null,
            Payload: payload,
            Template: null,
            Metadata: null);

    private static GenerateDocumentHttpRequest BinaryRequest(string payloadBase64)
        => TextRequest(null) with { PayloadBase64 = payloadBase64 };
}
