using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

[Collection("integration")]
public sealed class McpAuthDiscoveryTests(ContactApiTestFactory factory) : McpResourceMetadataTests
{
    protected override HttpClient CreateAnonymousClient() => factory.AnonymousClient();

    protected override string Issuer => factory.Authority!;

    public override Task InitializeAsync() => factory.ResetAsync();
}
