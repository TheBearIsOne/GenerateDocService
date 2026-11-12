using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GenerateDocService.Api.IntegrationTests;

/// <summary>
/// A real HTTP listener on a random loopback port that stands in for an external document API. It
/// exposes the FOP converter's endpoint shape unauthenticated, plus Basic, static Bearer, and OAuth2
/// client-credentials variants, so the <c>api</c> engine can be exercised against every scheme
/// without Docker or the real converter.
/// </summary>
public sealed class MockApiService : IAsyncLifetime
{
    public const string BasicUsername = "gds-user";
    public const string BasicPassword = "gds-secret-password";
    public const string StaticBearerToken = "static-bearer-token";
    public const string OAuthClientId = "gds-client";
    public const string OAuthClientSecret = "gds-client-secret";
    public const string OAuthScope = "documents.convert";
    public const string OAuthIssuedToken = "oauth-issued-access-token";

    /// <summary>Deliberately wrong, so a test can prove bad credentials fail rather than pass silently.</summary>
    public const string WrongPassword = "not-the-password";

    private WebApplication? _app;
    private int _tokenEndpointHits;
    private int _convertHits;

    public string BaseUrl { get; private set; } = string.Empty;

    public int TokenEndpointHits => Volatile.Read(ref _tokenEndpointHits);

    public int ConvertHits => Volatile.Read(ref _convertHits);

    /// <summary>Query string of the most recent call to the FOP-shaped endpoint.</summary>
    public string? LastConvertQuery { get; private set; }

    /// <summary>Form field name of the most recent multipart upload, proving the wire format.</summary>
    public string? LastMultipartFieldName { get; private set; }

    /// <summary>Why the multipart endpoint rejected the last request, if it did.</summary>
    public string? LastMultipartError { get; private set; }

    public void ResetCounters()
    {
        Interlocked.Exchange(ref _tokenEndpointHits, 0);
        Interlocked.Exchange(ref _convertHits, 0);
        LastConvertQuery = null;
        LastMultipartFieldName = null;
        LastMultipartError = null;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Development
        });

        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // Mirrors the FOP converter: no authentication, multipart or raw body in, PDF out.
        app.MapPost("/api/v1/convert/sync", async (HttpRequest request) =>
        {
            IncrementConvert();
            LastConvertQuery = request.QueryString.Value;
            return Results.Bytes(MockPdf(await ReadBodyAsync(request)), "application/pdf");
        });

        // Faithful to the real FOP converter contract: it only accepts a multipart upload with a
        // named primaryDocument part, and rejects a raw body. That is what makes the assertion on
        // LastMultipartFieldName meaningful rather than a tautology.
        app.MapPost("/multipart/convert", async (HttpRequest request) =>
        {
            if (!request.HasFormContentType)
            {
                LastMultipartError = "request was not multipart/form-data";
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            var form = await request.ReadFormAsync();
            var file = form.Files["primaryDocument"];
            if (file is null)
            {
                LastMultipartError = "no part named primaryDocument";
                return Results.StatusCode(StatusCodes.Status400BadRequest);
            }

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer);

            IncrementConvert();
            LastMultipartFieldName = file.Name;
            LastConvertQuery = request.QueryString.Value;

            return Results.Bytes(MockPdf(buffer.ToArray()), "application/pdf");
        });

        app.MapPost("/basic/convert", async (HttpRequest request) =>
        {
            if (!HasBasicCredential(request, BasicUsername, BasicPassword))
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            IncrementConvert();
            return Results.Bytes(MockPdf(await ReadBodyAsync(request)), "application/pdf");
        });

        app.MapPost("/bearer/convert", async (HttpRequest request) =>
        {
            if (!HasBearerToken(request, StaticBearerToken))
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            IncrementConvert();
            return Results.Bytes(MockPdf(await ReadBodyAsync(request)), "application/pdf");
        });

        app.MapPost("/oauth2/token", async (HttpRequest request) =>
        {
            Interlocked.Increment(ref _tokenEndpointHits);

            var form = await request.ReadFormAsync();
            if (form["grant_type"] != "client_credentials"
                || form["client_id"] != OAuthClientId
                || form["client_secret"] != OAuthClientSecret)
            {
                return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            return Results.Json(new
            {
                access_token = OAuthIssuedToken,
                expires_in = 3_600,
                token_type = "Bearer"
            });
        });

        app.MapPost("/oauth2/convert", async (HttpRequest request) =>
        {
            if (!HasBearerToken(request, OAuthIssuedToken))
            {
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            }

            IncrementConvert();
            return Results.Bytes(MockPdf(await ReadBodyAsync(request)), "application/pdf");
        });

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        BaseUrl = addresses?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("The mock API did not bind an address.");

        _app = app;
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    /// <summary>
    /// Configuration keys that register one target per authentication scheme, all pointing at this
    /// listener. Applied through <c>UseSetting</c> so the API host builds its real DI graph.
    /// </summary>
    public IReadOnlyDictionary<string, string?> BuildTargetSettings()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal);

        AddTarget(settings, "open", "api/v1/convert/sync?payloadType=XSL_FO&outputFormat=PDF&correlationId={correlationId}", "xsl-fo", "application/xml", "None");
        AddTarget(settings, "converter", "multipart/convert?payloadType=XSL_FO&outputFormat=PDF&correlationId={correlationId}", "xsl-fo", "application/xml", "None");
        AddTarget(settings, "basic", "basic/convert", "json", "application/json", "Basic");
        AddTarget(settings, "bearer", "bearer/convert", "json", "application/json", "Bearer");
        AddTarget(settings, "oauth", "oauth2/convert", "json", "application/json", "OAuth2ClientCredentials");
        AddTarget(settings, "basic-wrong", "basic/convert", "json", "application/json", "Basic");

        const string prefix = "DocumentProcessing:Api:Targets";

        settings[$"{prefix}:converter:MultipartFieldName"] = "primaryDocument";

        settings[$"{prefix}:basic:Authentication:Username"] = BasicUsername;
        settings[$"{prefix}:basic:Authentication:Password"] = BasicPassword;

        settings[$"{prefix}:bearer:Authentication:Token"] = StaticBearerToken;

        settings[$"{prefix}:oauth:Authentication:TokenUrl"] = $"{BaseUrl}/oauth2/token";
        settings[$"{prefix}:oauth:Authentication:ClientId"] = OAuthClientId;
        settings[$"{prefix}:oauth:Authentication:ClientSecret"] = OAuthClientSecret;
        settings[$"{prefix}:oauth:Authentication:Scope"] = OAuthScope;

        settings[$"{prefix}:basic-wrong:Authentication:Username"] = BasicUsername;
        settings[$"{prefix}:basic-wrong:Authentication:Password"] = WrongPassword;

        return settings;
    }

    private void AddTarget(
        IDictionary<string, string?> settings,
        string name,
        string requestPath,
        string inputFormat,
        string contentType,
        string scheme)
    {
        var prefix = $"DocumentProcessing:Api:Targets:{name}";

        settings[$"{prefix}:BaseUrl"] = BaseUrl;
        settings[$"{prefix}:RequestPath"] = requestPath;
        settings[$"{prefix}:InputFormat"] = inputFormat;
        settings[$"{prefix}:OutputFormat"] = "pdf";
        settings[$"{prefix}:ContentType"] = contentType;
        settings[$"{prefix}:TimeoutSeconds"] = "30";
        settings[$"{prefix}:Authentication:Scheme"] = scheme;
    }

    private void IncrementConvert() => Interlocked.Increment(ref _convertHits);

    private static bool HasBasicCredential(HttpRequest request, string username, string password)
    {
        var header = request.Headers.Authorization.FirstOrDefault();
        if (header is null || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            return decoded == $"{username}:{password}";
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool HasBearerToken(HttpRequest request, string expected)
    {
        var header = request.Headers.Authorization.FirstOrDefault();

        return header is not null
            && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && string.Equals(header["Bearer ".Length..], expected, StringComparison.Ordinal);
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Echoes the received byte count back into the payload so a test can prove the request body
    /// arrived intact rather than only that something came back.
    /// </summary>
    private static byte[] MockPdf(byte[] receivedBody)
        => Encoding.ASCII.GetBytes(
            $"%PDF-1.4\n% mock-api-service\n% received-bytes={receivedBody.Length}\n%%EOF\n");
}
