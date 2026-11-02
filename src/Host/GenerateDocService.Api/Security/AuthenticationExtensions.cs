using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace GenerateDocService.Api.Security;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddDocumentAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        AuthenticationOptions authOptions)
    {
        services.Configure<AuthenticationOptions>(
            configuration.GetSection(AuthenticationOptions.SectionName));

        // Always register authorization so UseAuthorization() doesn't throw
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.DocumentSubmit, policy =>
                policy.RequireRole(AuthorizationPolicies.DocumentSubmit, AuthorizationPolicies.DocumentAdmin))
            .AddPolicy(AuthorizationPolicies.DocumentRead, policy =>
                policy.RequireRole(AuthorizationPolicies.DocumentRead, AuthorizationPolicies.DocumentAdmin))
            .AddPolicy(AuthorizationPolicies.DocumentDownload, policy =>
                policy.RequireRole(AuthorizationPolicies.DocumentDownload, AuthorizationPolicies.DocumentAdmin))
            .AddPolicy(AuthorizationPolicies.DocumentAdmin, policy =>
                policy.RequireRole(AuthorizationPolicies.DocumentAdmin));

        if (!authOptions.Enabled)
        {
            return services;
        }

        if (string.IsNullOrWhiteSpace(authOptions.SigningKey))
        {
            throw new InvalidOperationException(
                "Authentication is enabled but no JWT signing key is configured. " +
                $"Set '{AuthenticationOptions.SectionName}:SigningKey' or the GDS_AUTH_SIGNING_KEY environment variable.");
        }

        if (authOptions.SigningKey.Length < AuthenticationOptions.MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"The JWT signing key must be at least {AuthenticationOptions.MinimumSigningKeyLength} characters " +
                $"for HS256, but was {authOptions.SigningKey.Length}. " +
                $"Update '{AuthenticationOptions.SectionName}:SigningKey' or the GDS_AUTH_SIGNING_KEY environment variable.");
        }

        var signingKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(authOptions.SigningKey));

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = authOptions.Issuer,
                    ValidAudience = authOptions.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromMinutes(2)
                };
            });

        return services;
    }
}
