using GenerateDocService.DocumentProcessing.Presentation.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace GenerateDocService.Api.Validation;

public sealed class ValidationRequestFilter : IEndpointFilter
{
    private readonly GenerateDocumentRequestValidator _validator;

    public ValidationRequestFilter(GenerateDocumentRequestValidator validator)
    {
        _validator = validator;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments
            .OfType<GenerateDocumentHttpRequest>()
            .FirstOrDefault();

#pragma warning disable CS8604
        var result = _validator.Validate(request);
#pragma warning restore CS8604

        if (!result.IsValid)
        {
            var errors = result.Errors
                .GroupBy(e => e.Field)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.Message).ToArray());

            return Results.ValidationProblem(errors);
        }

        return await next(context);
    }
}
