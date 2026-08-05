using CmsSync.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CmsSync.IntegrationTests.Controllers;

[Trait("Category", "Controllers")]
public sealed class ControllerRouteParityTests
{
    private const string SafeConnectionString =
        "Server=configuration-only.invalid;Database=configuration-only;Integrated Security=true";

    [Fact]
    public async Task MvcDiscoversExactlyTheFourBusinessControllerActions()
    {
        await using var factory = CreateFactory();

        var actionProvider = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var actions = actionProvider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(action => string.Equals(
                action.ControllerTypeInfo.AsType().Namespace,
                "CmsSync.Api.Controllers",
                StringComparison.Ordinal))
            .Select(action => new
            {
                Name = action.ControllerTypeInfo.Name + "." + action.MethodInfo.Name,
                Template = action.AttributeRouteInfo?.Template,
            })
            .OrderBy(action => action.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "CmsEntitiesController.GetEntityByIdAsync",
                "CmsEntitiesController.ListEntitiesAsync",
                "CmsEntitiesController.SetAdministrativeStateAsync",
                "CmsEventsController.ProcessEventsAsync",
            ],
            actions.Select(action => action.Name).ToArray());

        var templatesByAction = actions.ToDictionary(action => action.Name, action => action.Template, StringComparer.Ordinal);
        Assert.Equal("cms/events", templatesByAction["CmsEventsController.ProcessEventsAsync"]);
        Assert.Equal("api/entities", templatesByAction["CmsEntitiesController.ListEntitiesAsync"]);
        Assert.Equal("api/entities/{entityId}", templatesByAction["CmsEntitiesController.GetEntityByIdAsync"]);
        Assert.Equal(
            "api/entities/{entityId}/administrative-state",
            templatesByAction["CmsEntitiesController.SetAdministrativeStateAsync"]);
    }

    [Fact]
    public async Task EndpointInventoryContainsSingleBusinessRoutesAndAnonymousHealthEndpoints()
    {
        await using var factory = CreateFactory();

        var routeEndpoints = factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => ExpandEndpointMethods(endpoint))
            .ToArray();

        var expectedBusinessRoutes = new (string Method, string Path)[]
        {
            (HttpMethods.Post, "/cms/events"),
            (HttpMethods.Get, "/api/entities"),
            (HttpMethods.Get, "/api/entities/{entityId}"),
            (HttpMethods.Put, "/api/entities/{entityId}/administrative-state"),
        };

        foreach (var expectedRoute in expectedBusinessRoutes)
        {
            var matches = routeEndpoints
                .Where(endpoint =>
                    string.Equals(endpoint.Method, expectedRoute.Method, StringComparison.Ordinal) &&
                    string.Equals(endpoint.Path, expectedRoute.Path, StringComparison.Ordinal))
                .ToArray();

            Assert.Single(matches);
        }

        var businessRouteMatches = routeEndpoints
            .Where(endpoint =>
                string.Equals(endpoint.Path, "/cms/events", StringComparison.Ordinal) ||
                endpoint.Path.StartsWith("/api/entities", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(expectedBusinessRoutes.Length, businessRouteMatches.Length);

        AssertHealthEndpoint(routeEndpoints, "/health/live");
        AssertHealthEndpoint(routeEndpoints, "/health/ready");
    }

    private static void AssertHealthEndpoint(
        IReadOnlyCollection<(string Method, string Path, RouteEndpoint Endpoint)> routeEndpoints,
        string expectedPath)
    {
        var healthEndpoints = routeEndpoints
            .Where(endpoint => string.Equals(endpoint.Path, expectedPath, StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(healthEndpoints);
        Assert.Contains(
            healthEndpoints,
            endpoint =>
                string.Equals(endpoint.Method, HttpMethods.Get, StringComparison.Ordinal) ||
                string.Equals(endpoint.Method, "*", StringComparison.Ordinal));

        Assert.All(
            healthEndpoints,
            endpoint => Assert.Empty(endpoint.Endpoint.Metadata.OfType<IAuthorizeData>()));
    }

    private static IEnumerable<(string Method, string Path, RouteEndpoint Endpoint)> ExpandEndpointMethods(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;

        if (methods is null)
        {
            yield return ("*", NormalizeRouteTemplate(endpoint.RoutePattern.RawText), endpoint);
            yield break;
        }

        var normalizedPath = NormalizeRouteTemplate(endpoint.RoutePattern.RawText);

        foreach (var method in methods)
        {
            yield return (method.ToUpperInvariant(), normalizedPath, endpoint);
        }
    }

    private static string NormalizeRouteTemplate(string? rawText)
    {
        var route = rawText ?? string.Empty;

        if (!route.StartsWith('/'))
        {
            route = "/" + route;
        }

        if (route.Length > 1 && route.EndsWith('/'))
        {
            route = route[..^1];
        }

        return route;
    }

    private static CmsSyncWebApplicationFactory CreateFactory()
    {
        return new CmsSyncWebApplicationFactory(
            SafeConnectionString,
            SafeConnectionString,
            environmentName: "Development");
    }
}
