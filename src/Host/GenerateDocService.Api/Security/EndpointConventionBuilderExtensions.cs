namespace GenerateDocService.Api.Security;

public static class EndpointConventionBuilderExtensions
{
    public static IEndpointConventionBuilder MaybeRequireAuth(
        this IEndpointConventionBuilder builder,
        string policy,
        bool authEnabled)
        => authEnabled ? builder.RequireAuthorization(policy) : builder;
}
