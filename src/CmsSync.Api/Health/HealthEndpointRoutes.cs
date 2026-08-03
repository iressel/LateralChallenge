namespace CmsSync.Api.Health;

public static class HealthEndpointRoutes
{
    public const string Prefix = "/health";
    public const string Liveness = Prefix + "/live";
    public const string Readiness = Prefix + "/ready";
}
