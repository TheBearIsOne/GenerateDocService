using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

public sealed class AuthenticationAuthorizationIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SigningKey = "integration-test-signing-key-long-enough-for-hs256";
    private const string Issuer = "GenerateDocService";
    private const string Audience = "GenerateDocService.Api";

    private readonly WebApplicationFactory<Program> _factory;

    public AuthenticationAuthorizationIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DocumentProcessing:Authentication:Enabled", "true");
            builder.UseSetting("DocumentProcessing:Authentication:SigningKey", SigningKey);
            builder.UseSetting("DocumentProcessing:Authentication:Issuer", Issuer);
            builder.UseSetting("DocumentProcessing:Authentication:Audience", Audience);
        });
    }

    [Fact]
    public async Task Engines_ShouldReturn401_WhenNoTokenIsSupplied()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/engines");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Engines_ShouldReturn200_WithTheDocumentReadRole()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer("DocumentRead");

        var response = await client.GetAsync("/api/v1/engines");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Engines_ShouldReturn403_WhenTheRoleDoesNotSatisfyThePolicy()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer("DocumentSubmit");

        var response = await client.GetAsync("/api/v1/engines");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DocumentAdmin_ShouldSatisfyEveryPolicy()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer("DocumentAdmin");

        (await client.GetAsync("/api/v1/engines")).StatusCode.Should().Be(HttpStatusCode.OK);

        var generated = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: """{"document":"hello"}""",
            Template: "Document: {{ document }}",
            Metadata: null));

        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await generated.Content.ReadAsStringAsync()).Should().Contain("Document: hello");
    }

    [Fact]
    public async Task SyncGeneration_ShouldReturn403_WithOnlyTheDocumentReadRole()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer("DocumentRead");

        var response = await client.PostAsJsonAsync("/api/v1/documents/sync", new GenerateDocumentHttpRequest(
            RequestId: Guid.NewGuid().ToString("N"),
            Engine: "scriban",
            InputFormat: "json",
            OutputFormat: "txt",
            TemplateFormat: "scriban",
            Payload: """{"document":"hello"}""",
            Template: "Document: {{ document }}",
            Metadata: null));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Token_ShouldBeRejected_WhenSignedWithADifferentKey()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer("DocumentAdmin", "a-completely-different-signing-key-32-chars-min");

        var response = await client.GetAsync("/api/v1/engines");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_ShouldBeRejected_AfterItExpires()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer("DocumentAdmin", SigningKey, ttlMinutes: -10);

        var response = await client.GetAsync("/api/v1/engines");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthEndpoints_ShouldStayAnonymous()
    {
        using var client = _factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void Startup_ShouldFail_WhenTheSigningKeyIsShorterThanTheMinimum()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DocumentProcessing:Authentication:Enabled", "true");
            builder.UseSetting("DocumentProcessing:Authentication:SigningKey", "too-short");
        });

        var exception = Record.Exception(() => factory.CreateClient());

        exception.Should().NotBeNull("a missing or weak signing key must not silently degrade to a well-known key");
        Describe(exception!).Should().Contain("at least 32 characters");
    }

    private static AuthenticationHeaderValue Bearer(string role, string? signingKey = null, int ttlMinutes = 60)
        => new("Bearer", CreateToken(role, signingKey ?? SigningKey, ttlMinutes));

    private static string CreateToken(string role, string signingKey, int ttlMinutes)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = "integration-test",
            ["role"] = role,
            ["nbf"] = now,
            ["iat"] = now,
            ["exp"] = now + (ttlMinutes * 60)
        });

        var header = ToBase64Url(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var body = ToBase64Url(Encoding.UTF8.GetBytes(payload));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        var signature = ToBase64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{header}.{body}")));

        return $"{header}.{body}.{signature}";
    }

    private static string ToBase64Url(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Describe(Exception exception)
    {
        var builder = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            builder.Append(current.Message).Append(' ');
        }

        return builder.ToString();
    }
}
