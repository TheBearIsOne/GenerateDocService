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
