using Lupira.Testing.Mcp;
using Lupira.Testing.Postgres;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Over the real MCP transport, a call with names the tool schema doesn't declare is refused with a readable
/// error instead of an opaque one or a silently dropped field.</summary>
[Collection("integration")]
public sealed class McpToolArgumentsTests(ContactApiTestFactory factory) : McpStrictArgumentsTests
{
    protected override HttpClient CreateAuthenticatedClient() => factory.ApiClient("alice@x.test");

    protected override string DeclaredToolName => "resolve_contacts";

    protected override IReadOnlyDictionary<string, object?> DeclaredToolArguments =>
        new Dictionary<string, object?> { ["request"] = new Dictionary<string, object?> { ["Names"] = new[] { "Nobody" } } };

    public override Task InitializeAsync() => factory.ResetAsync();

    [Fact]
    public async Task Unwrapped_request_fields_name_the_wrapper()
    {
        await using var mcp = await ConnectAsync();
        var result = await mcp.CallToolAsync("resolve_contacts", new Dictionary<string, object?> { ["names"] = new[] { "Adrian" } });

        Assert.Equal("Invalid arguments for 'resolve_contacts': unknown names; missing required request. Accepts: request (required).",
            ErrorText(result));
    }

    [Fact]
    public async Task Misnamed_batch_item_field_is_rejected_with_its_path()
    {
        await using var mcp = await ConnectAsync();
        var item = new Dictionary<string, object?> { ["bogus"] = 1 };
        var result = await mcp.CallToolAsync("create_contacts_batch",
            new Dictionary<string, object?> { ["request"] = new Dictionary<string, object?> { ["contacts"] = new[] { item } } });

        var text = ErrorText(result);
        Assert.Contains("unknown request.contacts[0].bogus", text);
        Assert.Contains("At request.contacts[]: accepts ", text);
    }
}
