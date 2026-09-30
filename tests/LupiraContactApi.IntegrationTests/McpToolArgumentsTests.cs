using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Over the real MCP transport, a call with names the tool schema doesn't declare is refused with a readable
/// error instead of an opaque one or a silently dropped field.</summary>
public sealed class McpToolArgumentsTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private async Task<McpClient> ConnectAsync()
    {
        var http = Factory.ApiClient("alice@x.test");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string ErrorText(CallToolResult result)
    {
        Assert.True(result.IsError);
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }

    [Fact]
    public async Task Unwrapped_request_fields_name_the_wrapper()
    {
        await using var mcp = await ConnectAsync();
        var result = await mcp.CallToolAsync("resolve_contacts", new Dictionary<string, object?> { ["names"] = new[] { "Adrian" } });

        Assert.Equal("Invalid arguments for 'resolve_contacts': unknown names; missing required request. Accepts: request (required).",
            ErrorText(result));
    }

    [Fact]
    public async Task Nested_fields_bind_case_insensitively()
    {
        await using var mcp = await ConnectAsync();
        var result = await mcp.CallToolAsync("resolve_contacts",
            new Dictionary<string, object?> { ["request"] = new Dictionary<string, object?> { ["Names"] = new[] { "Nobody" } } });

        Assert.NotEqual(true, result.IsError);
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

    [Fact]
    public async Task Every_tool_rejects_an_undeclared_argument()
    {
        await using var mcp = await ConnectAsync();
        var tools = await mcp.ListToolsAsync();
        Assert.NotEmpty(tools);
        foreach (var tool in tools)
        {
            var result = await mcp.CallToolAsync(tool.Name, new Dictionary<string, object?> { ["__undeclared"] = 1 });

            Assert.StartsWith($"Invalid arguments for '{tool.Name}': unknown __undeclared", ErrorText(result));
        }
    }
}
