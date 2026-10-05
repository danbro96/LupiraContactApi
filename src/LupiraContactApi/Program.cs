using Lupira.Auth.Jwt;
using Lupira.Hosting.Defaults;
using Lupira.Hosting.Health;
using Lupira.Hosting.LanEdge;
using Lupira.Hosting.Observability;
using Lupira.Hosting.OpenApi;
using Lupira.Hosting.Problems;
using Lupira.Identity.Marten.AspNetCore;
using Lupira.Mcp;
using Lupira.Postgres.Health;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Dav;
using LupiraContactApi.Endpoints;
using LupiraContactApi.Handlers;
using LupiraContactApi.Mcp;
using Marten;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// --- Bounded context (data + transport-neutral services), registered from the Core class library.
// The connection string is read lazily from configuration (ConnectionStrings:Postgres) inside AddContactCore. ---
builder.Services.AddContactCore();

// --- Host-only services: identity (claims -> PrincipalDirectory) + the thin REST handlers. ---
builder.Services.AddLupiraCurrentUser<Principal>();
builder.Services.AddScoped<MeHandler>();
builder.Services.AddScoped<AddressBooksHandler>();
builder.Services.AddScoped<ContactsHandler>();
builder.Services.AddScoped<ContactGroupsHandler>();
builder.Services.AddScoped<InternalContactsHandler>();
builder.Services.AddScoped<SyncHandler>();
builder.Services.AddScoped<RelationshipsHandler>();
builder.Services.AddScoped<ResidenciesHandler>();
builder.Services.AddScoped<PlaceEntriesHandler>();
builder.Services.AddScoped<DavBackendHandler>();

// --- Auth: OIDC JWT for the REST/MCP surface; the /dav-backend seam additionally requires the DAV
//     gateway's client identity (azp). One identity authority (Authentik). ---
builder.AddLupiraJwt();
var apiSchemes = LupiraJwtSchemes.Api(builder.Environment);

builder.Services.AddAuthorizationBuilder()
    .AddLupiraApiPolicy(apiSchemes)
    .AddLupiraGatewayAzpPolicy(apiSchemes, builder.Configuration["DavGateway:ClientId"])
    .AddLupiraInternalScopePolicy(apiSchemes);

builder.AddLupiraTelemetry("lupira-contact-api");

builder.Services.AddLupiraHealth().AddReadyCheck<DatabaseReadyCheck>("postgres");

builder.AddLupiraDefaults(o =>
{
    o.CaseInsensitiveProperties = true;
    o.UtcDateTimeOffsets = true;
    o.ThrowOnBadRequest = true;
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
});

builder.Services.AddLupiraProblems();

builder.Services.AddOpenApi("v1", options => options.AddLupiraConventions(o =>
{
    o.Title = "Lupira Contact API";
    o.Description =
        "Contacts, address books, and kinship backend for Lupira. " +
        "Authenticate with a Bearer token issued by the OIDC provider (Authentik).";
    o.DropNullEnumMembers = true;
    o.DateTimeOffsetAsString = true;
}));

// MCP server for the agent, mounted at /mcp (LAN/WireGuard-only — not published through the tunnel).
builder.Services.AddLupiraMcp().WithTools<ContactTools>();

var app = builder.Build();

// Deliberate, one-shot schema apply (used as a deploy step: `dotnet LupiraContactApi.dll --apply-schema`).
if (args.Contains("--apply-schema"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    Console.WriteLine("Schema applied.");
    return;
}

// One-shot contact projection rebuild: recomputes every contact snapshot from the event log.
if (args.Contains("--rebuild-contacts"))
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    using var daemon = await store.BuildProjectionDaemonAsync();
    await daemon.RebuildProjectionAsync<Contact>(CancellationToken.None);
    Console.WriteLine("Contact projection rebuilt.");
    return;
}

// LAN-only surfaces (/mcp, /internal, /dav-backend): 404 anything arriving through the tunnel.
app.UseLanOnlySurfaces("/mcp", "/internal", "/dav-backend", "/.well-known/oauth-protected-resource");

app.UseLupiraDefaults();
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapLupiraOpenApi(o => o.Title = "Lupira Contact API");

app.MapLupiraHealth();

// REST surface (at root), one MapXxx per resource.
app.MapLupiraPing("ApiPolicy");
app.MapMe();
app.MapAddressBooks();
app.MapContacts();
app.MapContactGroups();
app.MapRelationships();
app.MapResidencies();
app.MapPlaceEntries();
app.MapSync();

// Service-to-service seams (LAN-only).
app.MapInternal();
app.MapDavBackend();

// Agent MCP transport (LAN/WireGuard-only; excluded from the Cloudflare Tunnel at the edge).
app.MapMcpResourceMetadata(app.Configuration["Auth:Oidc:Authority"]);
app.MapLupiraMcp();

app.Run();

// Exposes the implicit Program entry point to the integration test assembly (WebApplicationFactory<Program>).
public partial class Program;
