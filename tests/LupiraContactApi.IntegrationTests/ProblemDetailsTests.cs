using System.Net;
using System.Text.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The OpenAPI document promises every 4xx/5xx carries ProblemDetails. That promise rests on
/// UseStatusCodePages rather than on the endpoints' return types, so only a live response proves it —
/// and the document stays green whether or not the middleware is actually wired.</summary>
public class ProblemDetailsTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    [Fact]
    public async Task Missing_contact_returns_problem_details()
    {
        var api = Factory.ApiClient("anna@x.test");

        var resp = await api.GetAsync($"/contacts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Anonymous_rejection_returns_problem_details()
    {
        var resp = await Factory.AnonymousClient().GetAsync("/contacts");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    /// /mcp speaks JSON-RPC; rewriting its empty error bodies would corrupt the transport.
    [Fact]
    public async Task Mcp_challenge_is_not_rewritten()
    {
        var resp = await Factory.AnonymousClient().GetAsync("/mcp");

        Assert.NotEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }
}
