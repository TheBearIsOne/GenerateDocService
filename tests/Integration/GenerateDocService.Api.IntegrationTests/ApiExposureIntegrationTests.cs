using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// The compose stack used to run with ASPNETCORE_ENVIRONMENT=Development, which quietly turned on
/// OpenAPI, Scalar, detailed health output and the developer exception page. These pin the gating.
/// </summary>
public sealed class ApiExposureIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiExposureIntegrationTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Production_ShouldHideOpenApiAndHealthDetail_ByDefault()
    {
        using var client = CreateProductionClient(new Dictionary<string, string?>());

        var openApi = await client.GetAsync("/openapi/v1.json");
        openApi.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var health = await client.GetAsync("/health/live");
        health.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await health.Content.ReadAsStringAsync();
        body.Should().Be("Healthy");
        body.Should().NotContain("document-engines", "per-check detail leaks engine implementation names");
    }

    [Fact]
    public async Task Production_ShouldServeOpenApiAndHealthDetail_WhenExplicitlyEnabled()
    {
        using var client = CreateProductionClient(new Dictionary<string, string?>
        {
            ["DocumentProcessing:Api:ExposeOpenApi"] = "true",
            ["DocumentProcessing:Api:ExposeHealthDetails"] = "true"
        });

        (await client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await client.GetAsync("/health/live");
        health.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await health.Content.ReadAsStringAsync();
        body.Should().Contain("\"entries\"");
        body.Should().Contain("document-engines");
    }

    [Fact]
    public async Task Production_ShouldStillServeBothHealthProbes()
    {
        using var client = CreateProductionClient(new Dictionary<string, string?>());

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateProductionClient(Dictionary<string, string?> settings)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }
}
