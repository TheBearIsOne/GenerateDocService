using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using GenerateDocService.Engine.Api;
using Microsoft.Extensions.Options;

namespace GenerateDocService.DocumentProcessing.Application.Tests;

public sealed class DocumentProcessingApiOptionsTests
{
    [Fact]
    public void Validate_ShouldAcceptAWellFormedTargetPerScheme()
    {
        var options = new DocumentProcessingApiOptions
        {
            DefaultTarget = "open",
            Targets =
            {
                ["open"] = Target(authentication: new ApiAuthenticationOptions()),
                ["basic"] = Target(new ApiAuthenticationOptions
                {
                    Scheme = ApiAuthenticationScheme.Basic,
                    Username = "user",
                    Password = "pass"
                }),
                ["bearer"] = Target(new ApiAuthenticationOptions
                {
                    Scheme = ApiAuthenticationScheme.Bearer,
                    Token = "tok"
                }),
                ["oauth"] = Target(new ApiAuthenticationOptions
                {
                    Scheme = ApiAuthenticationScheme.OAuth2ClientCredentials,
                    TokenUrl = "https://id.example/token",
                    ClientId = "id",
                    ClientSecret = "secret"
                })
            }
        };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_ShouldRejectARelativeBaseUrl()
    {
        var options = new DocumentProcessingApiOptions
        {
            Targets = { ["t"] = Target() }
        };
        options.Targets["t"].BaseUrl = "converter.internal";

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*BaseUrl must be an absolute URI*");
    }

    [Fact]
    public void Validate_ShouldRejectAMissingRequestPath()
    {
        var options = new DocumentProcessingApiOptions { Targets = { ["t"] = Target() } };
        options.Targets["t"].RequestPath = "  ";

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RequestPath is required*");
    }

    [Fact]
    public void Validate_ShouldRejectANegativeTimeout()
    {
        var options = new DocumentProcessingApiOptions { Targets = { ["t"] = Target() } };
        options.Targets["t"].TimeoutSeconds = -1;

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*TimeoutSeconds*");
    }

    [Fact]
    public void Validate_ShouldRejectAnUnknownDefaultTarget()
    {
        var options = new DocumentProcessingApiOptions
        {
            DefaultTarget = "missing",
            Targets = { ["t"] = Target() }
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*DefaultTarget 'missing'*");
    }

    [Theory]
    [InlineData(ApiAuthenticationScheme.Basic, null, "pass", "Basic requires both Username and Password")]
    [InlineData(ApiAuthenticationScheme.Basic, "user", null, "Basic requires both Username and Password")]
    [InlineData(ApiAuthenticationScheme.Bearer, null, null, "Bearer requires Token")]
    [InlineData(ApiAuthenticationScheme.OAuth2ClientCredentials, null, null, "OAuth2ClientCredentials requires an absolute TokenUrl")]
    public void Validate_ShouldRejectIncompleteAuthentication(
        ApiAuthenticationScheme scheme,
        string? username,
        string? password,
        string expectedMessage)
    {
        var options = new DocumentProcessingApiOptions
        {
            Targets =
            {
                ["t"] = Target(new ApiAuthenticationOptions
                {
                    Scheme = scheme,
                    Username = username,
                    Password = password
                })
            }
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{expectedMessage}*");
    }

    [Fact]
    public void Validate_ShouldRejectOAuth2WithoutClientCredentials()
    {
        var options = new DocumentProcessingApiOptions
        {
            Targets =
            {
                ["t"] = Target(new ApiAuthenticationOptions
                {
                    Scheme = ApiAuthenticationScheme.OAuth2ClientCredentials,
                    TokenUrl = "https://id.example/token",
                    ClientId = "id"
                })
            }
        };

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*requires ClientId and ClientSecret*");
    }

    private static ApiTargetOptions Target(ApiAuthenticationOptions? authentication = null)
        => new()
        {
            BaseUrl = "http://localhost:9999/",
            RequestPath = "api/v1/convert",
            Authentication = authentication ?? new ApiAuthenticationOptions()
        };
}

public sealed class ApiAuthTokenProviderTests
{
    [Fact]
    public async Task None_ShouldProduceNoHeader()
    {
        var provider = CreateProvider(out _, out _);

        var header = await provider.GetAsync(new ApiAuthenticationOptions());

        header.Should().BeNull();
    }

    [Fact]
    public async Task Basic_ShouldEncodeUsernameAndPassword()
    {
        var provider = CreateProvider(out _, out _);

        var header = await provider.GetAsync(new ApiAuthenticationOptions
        {
            Scheme = ApiAuthenticationScheme.Basic,
            Username = "ada",
            Password = "s3cret"
        });

        header!.Scheme.Should().Be("Basic");
        header.Parameter.Should().Be(Convert.ToBase64String(Encoding.UTF8.GetBytes("ada:s3cret")));
    }

    [Fact]
    public async Task Bearer_ShouldPassTheConfiguredTokenThrough()
    {
        var provider = CreateProvider(out _, out _);

        var header = await provider.GetAsync(new ApiAuthenticationOptions
        {
            Scheme = ApiAuthenticationScheme.Bearer,
            Token = "static-token"
        });

        header!.Scheme.Should().Be("Bearer");
        header.Parameter.Should().Be("static-token");
    }

    [Fact]
    public async Task OAuth2_ShouldFetchATokenAndCacheItUntilItNearExpiry()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = CreateProvider(out var handler, out _, clock);
        var authentication = OAuth2Authentication();

        var first = await provider.GetAsync(authentication);
        var second = await provider.GetAsync(authentication);

        first!.Parameter.Should().Be("issued-token");
        second!.Parameter.Should().Be("issued-token");
        handler.Calls.Should().Be(1, "the second call must be served from the cache");

        // expires_in is 600 with a 30 second skew, so the token is still valid at 569 seconds.
        clock.Now = clock.Now.AddSeconds(569);
        await provider.GetAsync(authentication);
        handler.Calls.Should().Be(1);

        clock.Now = clock.Now.AddSeconds(2);
        await provider.GetAsync(authentication);
        handler.Calls.Should().Be(2, "the token must be refreshed once the skew window is reached");
    }

    [Fact]
    public async Task OAuth2_ShouldSendClientCredentialsAsFormFields()
    {
        var provider = CreateProvider(out var handler, out var captured, new MutableClock(DateTimeOffset.UnixEpoch));

        await provider.GetAsync(OAuth2Authentication());

        captured.Should().NotBeNull();
        captured!["grant_type"].Should().Be("client_credentials");
        captured["client_id"].Should().Be("client-id");
        captured["client_secret"].Should().Be("client-secret");
        captured["scope"].Should().Be("documents.convert");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task OAuth2_ShouldThrowWhenTheTokenEndpointOmitsAccessToken()
    {
        var provider = CreateProvider(out _, out _, responseJson: """{"error":"invalid_client"}""");

        var act = () => provider.GetAsync(OAuth2Authentication());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*did not return an 'access_token'*");
    }

    [Fact]
    public async Task OAuth2_ShouldReadExpiresInWhenItIsAString()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var provider = CreateProvider(out var handler, out _, clock,
            responseJson: """{"access_token":"issued-token","expires_in":"600"}""");

        await provider.GetAsync(OAuth2Authentication());
        clock.Now = clock.Now.AddSeconds(300);
        await provider.GetAsync(OAuth2Authentication());

        handler.Calls.Should().Be(1);
    }

    private static ApiAuthenticationOptions OAuth2Authentication()
        => new()
        {
            Scheme = ApiAuthenticationScheme.OAuth2ClientCredentials,
            TokenUrl = "https://id.example/token",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            Scope = "documents.convert"
        };

    private static ApiAuthTokenProvider CreateProvider(
        out StubHttpMessageHandler handler,
        out CapturedForm captured,
        TimeProvider? timeProvider = null,
        string responseJson = """{"access_token":"issued-token","expires_in":600,"token_type":"Bearer"}""")
    {
        captured = new CapturedForm();
        handler = new StubHttpMessageHandler(responseJson) { LastForm = captured };

        var client = new HttpClient(handler);
        return new ApiAuthTokenProvider(
            new SingleClientFactory(client),
            timeProvider ?? TimeProvider.System);
    }

    private sealed class CapturedForm : Dictionary<string, string>
    {
    }

    private sealed class StubHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public CapturedForm? LastForm { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);

            if (request.Content is not null && LastForm is not null)
            {
                var raw = await request.Content.ReadAsStringAsync(cancellationToken);
                foreach (var pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var separator = pair.IndexOf('=');
                    if (separator > 0)
                    {
                        LastForm[Uri.UnescapeDataString(pair[..separator])] =
                            Uri.UnescapeDataString(pair[(separator + 1)..]);
                    }
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class MutableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

public sealed class ApiDocumentGenerationEngineCapabilityTests
{
    [Fact]
    public void CanHandle_ShouldFollowTheConfiguredTargets()
    {
        var engine = CreateEngine(new DocumentProcessingApiOptions
        {
            Targets =
            {
                ["t"] = new ApiTargetOptions
                {
                    BaseUrl = "http://localhost:9999/",
                    RequestPath = "convert",
                    InputFormat = "xsl-fo",
                    OutputFormat = "pdf"
                }
            }
        });

        engine.Name.Should().Be("api");
        engine.CanHandle("xsl-fo", "pdf").Should().BeTrue();
        engine.CanHandle("json", "pdf").Should().BeFalse();
        engine.CanHandle("xsl-fo", "txt").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_ShouldRejectAnyTemplateFormat()
    {
        var engine = CreateEngine(new DocumentProcessingApiOptions
        {
            Targets = { ["t"] = new ApiTargetOptions { BaseUrl = "http://localhost:9999/", RequestPath = "convert" } }
        });

        // A target with no declared formats matches anything, except a request carrying a template.
        engine.CanHandle("anything", "whatever").Should().BeTrue();
        engine.CanHandle("json", "pdf", "scriban").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_ShouldBeFalseWhenNoTargetsAreConfigured()
    {
        var engine = CreateEngine(new DocumentProcessingApiOptions());

        engine.CanHandle("json", "pdf").Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_ShouldFailTerminally_WhenTheUpstreamIsUnreachable()
    {
        var engine = CreateEngine(new DocumentProcessingApiOptions
        {
            Targets =
            {
                ["dead"] = new ApiTargetOptions
                {
                    BaseUrl = "http://127.0.0.1:1/",
                    RequestPath = "convert",
                    TimeoutSeconds = 10
                }
            }
        });

        var request = new GenerateDocService.DocumentProcessing.Application.Models.GenerateDocumentRequest(
            requestId: "api-dead",
            engine: "api",
            inputFormat: "json",
            outputFormat: "pdf",
            templateFormat: null,
            payload: Encoding.UTF8.GetBytes("{}"),
            template: null,
            metadata: new Dictionary<string, string> { [DocumentProcessingApiOptions.TargetMetadataKey] = "dead" });

        var act = () => engine.GenerateAsync(request);

        // Not retryable: the request may have been received before the failure, and the upstream is
        // a third party whose idempotency is unknown.
        await act.Should().ThrowAsync<GenerateDocService.Engine.Abstractions.NonRetryableGenerationException>()
            .WithMessage("*'dead' failed*");
    }

    private static ApiDocumentGenerationEngine CreateEngine(DocumentProcessingApiOptions options)
        => new(
            new NullHttpClientFactory(),
            Options.Create(options),
            new NullAuthTokenProvider());

    private sealed class NullHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class NullAuthTokenProvider : IApiAuthTokenProvider
    {
        public Task<AuthenticationHeaderValue?> GetAsync(
            ApiAuthenticationOptions authentication,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AuthenticationHeaderValue?>(null);
    }
}
