using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GenerateDocService.Engine.Api;

public interface IApiAuthTokenProvider
{
    /// <summary>Returns the header to attach, or null when the target is unauthenticated.</summary>
    Task<AuthenticationHeaderValue?> GetAsync(
        ApiAuthenticationOptions authentication,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the Authorization header for each configured scheme. Client-credentials tokens are cached
/// per token endpoint and client, and refreshed ahead of expiry, so a busy target does not hit the
/// token endpoint on every document.
/// </summary>
public sealed class ApiAuthTokenProvider : IApiAuthTokenProvider
{
    public const string TokenClientName = "api-engine-token";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Two constructors so DI resolves whichever it can satisfy without depending on TimeProvider
    // being registered, while tests can still inject a controllable clock.
    public ApiAuthTokenProvider(IHttpClientFactory httpClientFactory)
        : this(httpClientFactory, TimeProvider.System)
    {
    }

    public ApiAuthTokenProvider(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<AuthenticationHeaderValue?> GetAsync(
        ApiAuthenticationOptions authentication,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        return authentication.Scheme switch
        {
            ApiAuthenticationScheme.None => null,
            ApiAuthenticationScheme.Basic => Basic(authentication),
            ApiAuthenticationScheme.Bearer => new AuthenticationHeaderValue("Bearer", authentication.Token),
            ApiAuthenticationScheme.OAuth2ClientCredentials => new AuthenticationHeaderValue(
                "Bearer",
                await GetClientCredentialsTokenAsync(authentication, cancellationToken)),
            _ => throw new InvalidOperationException($"Unsupported API authentication scheme '{authentication.Scheme}'.")
        };
    }

    private static AuthenticationHeaderValue Basic(ApiAuthenticationOptions authentication)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{authentication.Username}:{authentication.Password}"));

        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private async Task<string> GetClientCredentialsTokenAsync(
        ApiAuthenticationOptions authentication,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.Join(
            '|',
            authentication.TokenUrl,
            authentication.ClientId,
            authentication.Scope ?? string.Empty);

        if (TryGetCachedToken(cacheKey, out var token))
        {
            return token!;
        }

        // One flight at a time: without this, a burst of requests against a cold cache would each
        // independently call the token endpoint.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedToken(cacheKey, out token))
            {
                return token!;
            }

            var fetched = await RequestTokenAsync(authentication, cancellationToken);
            _tokens[cacheKey] = fetched;

            return fetched.Token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetCachedToken(string cacheKey, out string? token)
    {
        if (_tokens.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > _timeProvider.GetUtcNow())
        {
            token = cached.Token;
            return true;
        }

        token = null;
        return false;
    }

    private async Task<CachedToken> RequestTokenAsync(
        ApiAuthenticationOptions authentication,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(TokenClientName);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = authentication.ClientId!,
            ["client_secret"] = authentication.ClientSecret!
        };

        if (!string.IsNullOrWhiteSpace(authentication.Scope))
        {
            form["scope"] = authentication.Scope;
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(authentication.TokenUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = payload.RootElement;

        if (!root.TryGetProperty("access_token", out var accessToken)
            || accessToken.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(accessToken.GetString()))
        {
            throw new InvalidOperationException(
                $"The token endpoint '{authentication.TokenUrl}' did not return an 'access_token'.");
        }

        var expiresInSeconds = ReadExpiresIn(root);
        var skew = Math.Max(0, authentication.TokenExpirySkewSeconds);
        var lifetime = TimeSpan.FromSeconds(Math.Max(0, expiresInSeconds - skew));

        return new CachedToken(accessToken.GetString()!, _timeProvider.GetUtcNow().Add(lifetime));
    }

    private static long ReadExpiresIn(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var expiresIn))
        {
            return 3_600;
        }

        // Some identity providers return expires_in as a string rather than a number.
        if (expiresIn.ValueKind == JsonValueKind.Number && expiresIn.TryGetInt64(out var seconds))
        {
            return seconds;
        }

        if (expiresIn.ValueKind == JsonValueKind.String
            && long.TryParse(expiresIn.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 3_600;
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAtUtc);
}
