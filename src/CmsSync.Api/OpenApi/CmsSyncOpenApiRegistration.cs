using CmsSync.Infrastructure.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace CmsSync.Api.OpenApi;

public static class CmsSyncOpenApiRegistration
{
    public static IServiceCollection AddCmsSyncOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(
                "v1",
                new OpenApiInfo
                {
                    Title = "CMS Sync API",
                    Version = "v1",
                    Description = "Receives CMS event batches and serves role-scoped entity and administrative state operations.",
                });

            options.AddSecurityDefinition(
                AuthenticationConstants.CmsScheme,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "basic",
                    Description = "Basic authentication for POST /cms/events.",
                });

            options.AddSecurityDefinition(
                AuthenticationConstants.ConsumerScheme,
                new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "basic",
                    Description =
                        "Basic authentication for entity endpoints. Administrative state updates require the Administrator role and return 403 for normal consumers.",
                });

            options.CustomOperationIds(apiDescription =>
            {
                if (apiDescription.ActionDescriptor is not ControllerActionDescriptor controllerAction)
                {
                    return null;
                }

                return controllerAction.MethodInfo.Name switch
                {
                    "ProcessEventsAsync" => "ProcessCmsEvents",
                    "ListEntitiesAsync" => "ListCmsEntities",
                    "GetEntityByIdAsync" => "GetCmsEntityById",
                    "SetAdministrativeStateAsync" => "SetCmsEntityAdministrativeState",
                    _ => controllerAction.MethodInfo.Name,
                };
            });

            options.OperationFilter<CmsSyncOpenApiOperationFilter>();
        });

        return services;
    }

    public static IApplicationBuilder UseCmsSyncSwagger(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "swagger";
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "CMS Sync API v1");
        });

        return app;
    }
}
