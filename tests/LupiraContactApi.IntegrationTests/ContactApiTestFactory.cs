using Lupira.Testing.Postgres;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LupiraContactApi.IntegrationTests;

public sealed class ContactApiTestFactory : LupiraApiFactory<Program>
{
    public IDocumentStore Store => Services.GetRequiredService<IDocumentStore>();

    protected override string AuthentikSlug => "lupira-contact";

    protected override Task ApplySchemaAsync() => Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

    protected override Task ResetDataAsync() => Store.Advanced.ResetAllData();
}
