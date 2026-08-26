using LupiraContactApi.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace LupiraContactApi.Endpoints;

/// <summary>Liveness (<c>/livez</c>) and readiness (<c>/readyz</c>) probes. Liveness = process-up
/// only; readiness = Postgres reachable.</summary>
public static class HealthChecks
{
    private const string ReadyTag = "ready";

    public static IServiceCollection AddAppHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<DatabaseReadyCheck>("postgres", tags: [ReadyTag]);
        return services;
    }

    public static void MapAppHealthChecks(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/livez", new HealthCheckOptions { Predicate = _ => false })
            .DisableHttpMetrics();
        app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) })
            .DisableHttpMetrics();
    }
}
