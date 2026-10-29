using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Kalandra.Api.Infrastructure;

public class AuthorizeOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var methodInfo = context.MethodInfo;
        var hasAuthorize = methodInfo.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any()
                           || methodInfo.DeclaringType?.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any() == true;

        var hasAllowAnonymous = methodInfo.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

        if (!hasAuthorize || hasAllowAnonymous)
            return;

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = ["Bearer"]
        });
    }
}
