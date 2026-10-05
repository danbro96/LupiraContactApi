using Lupira.Testing.Postgres;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LupiraContactApi.IntegrationTests;

public sealed class ContactApiTestFactory : LupiraApiFactory<Program>
{
    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    protected override string AuthentikSlug => "lupira-contact";

    // Tests read their own writes at once; the settle fence would hide them for its lag.
    protected override void AddSettings(IDictionary<string, string?> settings) => settings["Sync:SettleLag"] = TimeSpan.Zero.ToString();

    protected override Task ApplySchemaAsync() => Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

    protected override Task ResetDataAsync() => Store.Advanced.ResetAllData();
}
