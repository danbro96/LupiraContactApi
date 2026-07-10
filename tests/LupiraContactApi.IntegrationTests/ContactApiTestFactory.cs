using Marten;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LupiraContactApi.IntegrationTests;

/// <summary>
/// Hosts the real app against an ephemeral Postgres (Testcontainers). Runs in <c>Development</c> so the dev auth
/// handler is wired (<c>X-Dev-User</c>). Marten data is reset per test via <see cref="ResetAsync"/> so listings and
/// the global event sequence (the /dav-backend sync token) are deterministic.
/// </summary>
public sealed class ContactApiTestFactory : WebApplicationFactory<Program>
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private bool _schemaApplied;

    public ContactApiTestFactory()
    {
        _postgres.StartAsync().GetAwaiter().GetResult();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                // Never contacted (tests auth via X-Dev-User) — feeds the RFC 9728 metadata + JWT challenge.
                ["Auth:Authority"] = "https://auth.test/application/o/lupira-contact/",
            }));
    }

    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    public string ConnectionString => _postgres.GetConnectionString();

    /// <summary>Ensure the schema exists (once), then wipe all documents + events (and reset the event sequence).</summary>
    public async Task ResetAsync()
    {
        if (!_schemaApplied)
        {
            await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            _schemaApplied = true;
        }
        await Store.Advanced.ResetAllData();
    }

    public HttpClient ApiClient(string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", email);
        return client;
    }

    /// <summary>A client with no auth header — for asserting unauthenticated requests are rejected.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _postgres.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
