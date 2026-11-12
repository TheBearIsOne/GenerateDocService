using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using GenerateDocService.Engine.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// Owns both halves of the setup: the mock external API, and an API host whose configuration points
/// the <c>api</c> engine's targets at it. They live in one fixture because the target settings need
/// the mock's randomly assigned port, which is only known after it starts.
/// </summary>
public sealed class ApiEngineTestFixture : IAsyncLifetime
{
    private readonly MockApiService _mock = new();
    private WebApplicationFactory<Program>? _factory;

    public MockApiService Mock => _mock;

    public WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public async Task InitializeAsync()
    {
        await _mock.InitializeAsync();

        var settings = _mock.BuildTargetSettings();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        await _mock.DisposeAsync();
    }
}

public sealed class ApiEngineIntegrationTests : IClassFixture<ApiEngineTestFixture>
{
    private const string XslFoPayload = """
        <fo:root xmlns:fo="http://www.w3.org/1999/XSL/Format"><fo:layout-master-set><fo:simple-page-master master-name="d" page-height="11in" page-width="8.5in"><fo:region-body/></fo:simple-page-master></fo:layout-master-set><fo:page-sequence master-reference="d"><fo:flow flow-name="xsl-region-body"><fo:block>Hello</fo:block></fo:flow></fo:page-sequence></fo:root>
        """;

    private readonly ApiEngineTestFixture _fixture;

    public ApiEngineIntegrationTests(ApiEngineTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Api_ShouldBeRegisteredAsAnEngine()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/api/v1/engines");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var engines = await response.Content.ReadFromJsonAsync<DocumentEngineHttpResponse[]>();
        engines!.Select(e => e.Name).Should().Contain("api");
    }

    [Fact]
    public async Task UnauthenticatedTarget_ShouldReturnTheUpstreamDocument()
    {
        _fixture.Mock.ResetCounters();
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-open-1");

        var response = await PostAsync(client, "open", "xsl-fo", XslFoPayload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().StartWith("%PDF-1.4");
        body.Should().Contain($"received-bytes={Encoding.UTF8.GetByteCount(XslFoPayload)}",
            "the engine must forward the payload byte for byte");

        // Proves {correlationId} in the configured RequestPath is substituted per request.
        _fixture.Mock.LastConvertQuery.Should().Contain("correlationId=corr-open-1");
        _fixture.Mock.LastConvertQuery.Should().Contain("payloadType=XSL_FO");
    }

    [Fact]
    public async Task MultipartTarget_ShouldSendThePayloadAsANamedFormPart()
    {
        _fixture.Mock.ResetCounters();
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-multipart-1");

        var response = await PostAsync(client, "converter", "xsl-fo", XslFoPayload);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().StartWith("%PDF-1.4");

        // The mock answers a non-multipart body with 400, so reaching 200 already proves the engine
        // used multipart/form-data; this pins the part name the real FOP converter expects.
        _fixture.Mock.LastMultipartError.Should().BeNull();
        _fixture.Mock.LastMultipartFieldName.Should().Be("primaryDocument");
        _fixture.Mock.LastConvertQuery.Should().Contain("correlationId=corr-multipart-1");
    }

    [Fact]
    public async Task BasicAuthTarget_ShouldAuthenticateWithConfiguredCredentials()
    {
        using var client = CreateClient();

        var response = await PostAsync(client, "basic", "json", """{"document":"hello"}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().StartWith("%PDF-1.4");
    }

    [Fact]
    public async Task BearerTarget_ShouldAuthenticateWithConfiguredToken()
    {
        using var client = CreateClient();

        var response = await PostAsync(client, "bearer", "json", """{"document":"hello"}""");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().StartWith("%PDF-1.4");
    }

    [Fact]
    public async Task OAuth2Target_ShouldExchangeClientCredentialsOnceAndReuseTheToken()
    {
        // One test on purpose: ApiAuthTokenProvider is a singleton in the shared host fixture, so its
        // token cache outlives an individual test. The cold-cache assertion has to be made in the same
        // test that warms it. Expiry and refresh are covered deterministically in the unit tests.
        _fixture.Mock.ResetCounters();
        using var client = CreateClient();

        var first = await PostAsync(client, "oauth", "json", """{"document":"one"}""");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadAsStringAsync()).Should().StartWith("%PDF-1.4");
        _fixture.Mock.TokenEndpointHits.Should().Be(1, "the first call must exchange client credentials for a token");

        var second = await PostAsync(client, "oauth", "json", """{"document":"two"}""");
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Mock.ConvertHits.Should().Be(2);
        _fixture.Mock.TokenEndpointHits.Should().Be(1, "the token is cached until it nears expiry");
    }

    [Fact]
    public async Task WrongCredentials_ShouldFailRatherThanSilentlySucceed()
    {
        using var client = CreateClient();

        var response = await PostAsync(client, "basic-wrong", "json", """{"document":"hello"}""");

        // The upstream returns 401 and EnsureSuccessStatusCode turns that into a failed generation.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UnknownTargetName_ShouldBeRejected()
    {
        using var client = CreateClient();

        var response = await PostAsync(client, "no-such-target", "json", """{"document":"hello"}""");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no-such-target");
    }

    [Fact]
    public async Task TargetNameCannotRedirectToAnArbitraryHost()
    {
        using var client = CreateClient();

        // The target name selects a configured entry; there is no field through which a caller can
        // supply a URL, so this cannot be turned into a request to an arbitrary host.
        var response = await PostAsync(client, "http://169.254.169.254/latest/meta-data/", "json", "{}");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        _fixture.Mock.ConvertHits.Should().Be(0);
    }

    [Fact]
    public async Task ApiEngine_ShouldRejectATemplateFormat()
    {
        using var client = CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "api",
            InputFormat: "json",
            OutputFormat: "pdf",
            TemplateFormat: "scriban",
            Payload: """{"document":"hello"}""",
            Template: "{{ document }}",
            Metadata: new Dictionary<string, string> { [DocumentProcessingApiOptions.TargetMetadataKey] = "basic" }));

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private HttpClient CreateClient()
        => _fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string target,
        string inputFormat,
        string payload)
        => client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "api",
            InputFormat: inputFormat,
            OutputFormat: "pdf",
            TemplateFormat: null,
            Payload: payload,
            Template: null,
            Metadata: new Dictionary<string, string>
            {
                [DocumentProcessingApiOptions.TargetMetadataKey] = target
            }));
}
