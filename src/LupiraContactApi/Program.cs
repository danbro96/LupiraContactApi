using LupiraContactApi.Auth;
using LupiraContactApi.Dav;
using LupiraContactApi.Domain;
using LupiraContactApi.Endpoints;
using LupiraContactApi.Handlers;
using LupiraContactApi.Health;
using LupiraContactApi.Mcp;
using Marten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context (data + transport-neutral services), registered from the Core class library.
// The connection string is read lazily from configuration (ConnectionStrings:Postgres) inside AddContactCore. ---
builder.Services.AddContactCore();

// --- Host-only services: identity (claims -> Core PrincipalDirectory) + the thin REST handlers. ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<AddressBooksHandler>();
builder.Services.AddScoped<ContactsHandler>();
builder.Services.AddScoped<ContactGroupsHandler>();
builder.Services.AddScoped<InternalContactsHandler>();
builder.Services.AddScoped<SyncHandler>();
builder.Services.AddScoped<DavBackendHandler>();

// --- Auth: OIDC JWT for the REST/MCP surface; the /dav-backend seam additionally requires the DAV
//     gateway's client identity (azp). One identity authority (Authentik). ---
var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.Events = new JwtBearerEvents
        {
            // MCP auth spec: a 401 on /mcp advertises the RFC 9728 metadata so clients can discover the issuer.
            OnChallenge = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/mcp"))
                    ctx.Response.Headers.Append("WWW-Authenticate",
                        $"Bearer resource_metadata=\"{McpResourceMetadata.ResourceMetadataUrl(ctx.Request)}\"");
                return Task.CompletedTask;
            },
        };
    });

// Development-only: allow X-Dev-User header auth so the API can be exercised without Authentik.
if (builder.Environment.IsDevelopment())
    authBuilder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevAuthHandler.SchemeName, _ => { });

string[] apiSchemes = builder.Environment.IsDevelopment()
    ? [JwtBearerDefaults.AuthenticationScheme, DevAuthHandler.SchemeName]
    : [JwtBearerDefaults.AuthenticationScheme];

var davGatewayClientId = builder.Configuration["DavGateway:ClientId"];
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ApiPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser())
    // The DAV gateway's service identity: a valid token for this API (aud) minted by the gateway's
    // client (azp). Dev-header auth passes in Development so tests can drive the seam directly.
    .AddPolicy("DavBackendPolicy", p => p.AddAuthenticationSchemes(apiSchemes).RequireAuthenticatedUser()
        .RequireAssertion(ctx =>
            ctx.User.Identity?.AuthenticationType == DevAuthHandler.SchemeName
            || (davGatewayClientId is not null && ctx.User.HasClaim("azp", davGatewayClientId))));

// --- Observability: OpenTelemetry -> OpenObserve. Env-gated; the OTLP exporter reads OTEL_EXPORTER_OTLP_*
//     automatically (http/protobuf + Basic auth header set in compose). ---
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("lupira-contact-api"))
    .WithTracing(t =>
    {
        // Health probes are polled constantly by docker + devops-monitor; their spans add nothing.
        t.AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
            ctx.Request.Path != "/livez" && ctx.Request.Path != "/readyz");
        t.AddHttpClientInstrumentation();
        t.AddSource(Telemetry.ActivitySourceName);
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) t.AddOtlpExporter();
    })
    .WithMetrics(m =>
    {
        m.AddAspNetCoreInstrumentation();
        m.AddHttpClientInstrumentation();
        m.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint)) m.AddOtlpExporter();
    });

// Logs -> OpenObserve via OTLP, same env gate as traces/metrics.
builder.Logging.AddOpenTelemetry(o =>
{
    o.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("lupira-contact-api"));
    o.IncludeScopes = true;
    o.IncludeFormattedMessage = true;
    if (!string.IsNullOrWhiteSpace(otlpEndpoint)) o.AddOtlpExporter();
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyCheck>("postgres", tags: ["ready"]);

// Emit/accept enums as their names across the REST surface (not integers).
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.Converters.Add(new LupiraContactApi.Serialization.UtcDateTimeOffsetConverter());
});

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Info = new()
        {
            Title = "Lupira Contact API",
            Version = "v1",
            Description =
                "Contacts, address books, and kinship backend for Lupira. " +
                "Authenticate with a Bearer token issued by the OIDC provider (Authentik).",
        };
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "OIDC bearer token. Send as `Authorization: Bearer <token>`.",
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = endpointMetadata.OfType<IAuthorizeData>().Any()
                        && !endpointMetadata.OfType<IAllowAnonymous>().Any();
        if (requiresAuth)
        {
            operation.Security ??= new List<OpenApiSecurityRequirement>();
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = new List<string>(),
            });
        }
        return Task.CompletedTask;
    });
});

// MCP server for the agent, mounted at /mcp (LAN/WireGuard-only — not published through the tunnel).
builder.Services.AddMcpServer().WithHttpTransport().WithTools<ContactTools>();

var app = builder.Build();

// Deliberate, one-shot schema apply (used as a deploy step: `dotnet LupiraContactApi.dll --apply-schema`).
if (args.Contains("--apply-schema"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    Console.WriteLine("Schema applied.");
    return;
}

// One-shot contact projection rebuild (deploy step after the sync-surface release: pre-existing contact
// documents carry no UpdatedSequence watermark until their snapshots are recomputed from the event log).
if (args.Contains("--rebuild-contacts"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    using var daemon = await store.BuildProjectionDaemonAsync();
    await daemon.RebuildProjectionAsync<Contact>(CancellationToken.None);
    Console.WriteLine("Contact projection rebuilt.");
    return;
}

// Behind the Cloudflare Tunnel the public host differs from the container, so honor forwarded headers.
var forwarded = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
};
forwarded.KnownIPNetworks.Clear();
forwarded.KnownProxies.Clear();
app.UseForwardedHeaders(forwarded);

// LAN-only surfaces (/mcp, /internal, /dav-backend): 404 anything arriving through the tunnel.
app.UseLanOnlySurfaces();

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi("/openapi/{documentName}.json").AllowAnonymous();
app.MapScalarApiReference("/scalar", o => o
        .WithTitle("Lupira Contact API")
        .WithTheme(ScalarTheme.BluePlanet))
    .AllowAnonymous();

app.MapGet("/", () => TypedResults.Redirect("/scalar"))
   .ExcludeFromDescription()
   .AllowAnonymous();

// Health probes: /livez = liveness (no dependency checks); /readyz = readiness (Postgres reachable).
app.MapHealthChecks("/livez", new HealthCheckOptions { Predicate = _ => false })
    .DisableHttpMetrics();
app.MapHealthChecks("/readyz", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .DisableHttpMetrics();

// REST surface (at root), one MapXxx per resource.
app.MapMe();
app.MapAddressBooks();
app.MapContacts();
app.MapContactGroups();
app.MapSync();

// Service-to-service seams (LAN-only).
app.MapInternal();
app.MapDavBackend();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
app.MapMcpResourceMetadata(app.Configuration["Auth:Authority"]);
app.MapMcp("/mcp").RequireAuthorization("ApiPolicy");

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
