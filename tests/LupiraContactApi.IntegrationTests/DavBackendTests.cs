using LupiraContactApi.Dav;
using LupiraContactApi.Core.Domain.Identity;
using System.Net.Http.Json;
using System.Net;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>
/// The /dav-backend contract as the LupiraDavApi gateway consumes it: collection listing with JIT
/// provision + personal-book bootstrap, query/multiget, blob round-trip with ETag, PUT/DELETE with
/// preconditions, and the sync-token changes feed with tombstones.
/// </summary>
public sealed class DavBackendTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private static string Base(string email = Email) => $"/dav-backend/u/{Uri.EscapeDataString(email)}";

    [Fact]
    public async Task Collections_provision_the_principal_and_personal_book_on_first_sight()
    {
        var api = Factory.ApiClient(Email);
        var resp = await api.GetAsync($"{Base("fresh@x.test")}/collections");
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<DavCollectionsDto>();
        var book = Assert.Single(dto!.Collections);
        Assert.Equal(DavCollectionKind.AddressBook, book.Kind);
        Assert.Equal("Personal", book.DisplayName);
        Assert.StartsWith("seq-", book.Ctag);
        Assert.Equal("fresh@x.test", dto.Principal.DisplayName);
    }

    [Fact]
    public async Task Put_get_roundtrip_preserves_the_blob_identity_and_etag()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        var vcf = MinimalVcf("card-1@x", "Jane Doe", "jane@x.test");

        var put = await PutVcfAsync(api, Email, book, "card-1@x", vcf);
        Assert.Equal(HttpStatusCode.Created, put.StatusCode);
        var etag = put.Headers.ETag!.Tag.Trim('"');

        var get = await api.GetAsync($"{Base()}/collections/{book}/resources/card-1@x");
        get.EnsureSuccessStatusCode();
        Assert.Equal("text/vcard", get.Content.Headers.ContentType!.MediaType);
        Assert.Equal(etag, get.Headers.ETag!.Tag.Trim('"'));
        var body = await get.Content.ReadAsStringAsync();
        Assert.Contains("FN:Jane Doe", body);
        Assert.Contains("UID:card-1@x", body);
    }

    [Fact]
    public async Task Query_lists_uids_and_multiget_includes_content()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        await PutVcfAsync(api, Email, book, "a@x", MinimalVcf("a@x", "A One"));
        await PutVcfAsync(api, Email, book, "b@x", MinimalVcf("b@x", "B Two"));

        var listing = await api.PostAsJsonAsync($"{Base()}/collections/{book}/query", new DavQueryRequest());
        var all = (await listing.Content.ReadFromJsonAsync<DavResourcesDto>())!.Resources;
        Assert.Equal(2, all.Count);
        Assert.All(all, r => Assert.Null(r.Content));

        var multiget = await api.PostAsJsonAsync($"{Base()}/collections/{book}/query",
            new DavQueryRequest { Uids = ["a@x"], IncludeContent = true });
        var one = Assert.Single((await multiget.Content.ReadFromJsonAsync<DavResourcesDto>())!.Resources);
        Assert.Equal("a@x", one.Uid);
        Assert.Contains("FN:A One", one.Content);
    }

    [Fact]
    public async Task Put_preconditions_guard_create_and_update()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        var vcf = MinimalVcf("c@x", "C Three");

        var create = await PutVcfAsync(api, Email, book, "c@x", vcf, ifNoneMatchStar: true);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var etag = create.Headers.ETag!.Tag.Trim('"');

        // Duplicate create → 412.
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await PutVcfAsync(api, Email, book, "c@x", vcf, ifNoneMatchStar: true)).StatusCode);

        // Stale If-Match → 412; correct If-Match → 204 with a new etag.
        Assert.Equal(HttpStatusCode.PreconditionFailed,
            (await PutVcfAsync(api, Email, book, "c@x", MinimalVcf("c@x", "C Renamed"), ifMatch: "stale")).StatusCode);
        var update = await PutVcfAsync(api, Email, book, "c@x", MinimalVcf("c@x", "C Renamed"), ifMatch: etag);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.NotEqual(etag, update.Headers.ETag!.Tag.Trim('"'));
    }

    [Fact]
    public async Task Delete_honors_preconditions_and_tombstones_flow_to_changes()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        await PutVcfAsync(api, Email, book, "d@x", MinimalVcf("d@x", "D Four"));

        // Token before the delete.
        var before = await ChangesAsync(api, book, null);

        var del = new HttpRequestMessage(HttpMethod.Delete, $"{Base()}/collections/{book}/resources/d@x");
        del.Headers.TryAddWithoutValidation("If-Match", "\"wrong\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await api.SendAsync(del)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await api.DeleteAsync($"{Base()}/collections/{book}/resources/d@x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await api.DeleteAsync($"{Base()}/collections/{book}/resources/d@x")).StatusCode);   // benign retry

        var diff = await ChangesAsync(api, book, before.SyncToken);
        Assert.Contains("d@x", diff.Deleted);
        Assert.DoesNotContain(diff.Changed, c => c.Uid == "d@x");
    }

    [Fact]
    public async Task Changes_without_token_lists_all_live_and_with_token_diffs()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        await PutVcfAsync(api, Email, book, "e@x", MinimalVcf("e@x", "E Five"));

        var full = await ChangesAsync(api, book, null);
        Assert.Single(full.Changed, c => c.Uid == "e@x");
        Assert.Empty(full.Deleted);

        // No writes since → empty diff, token stable.
        var idle = await ChangesAsync(api, book, full.SyncToken);
        Assert.Empty(idle.Changed);
        Assert.Empty(idle.Deleted);

        await PutVcfAsync(api, Email, book, "f@x", MinimalVcf("f@x", "F Six"));
        var diff = await ChangesAsync(api, book, full.SyncToken);
        var only = Assert.Single(diff.Changed);
        Assert.Equal("f@x", only.Uid);

        // Garbage token degrades to the full listing (self-healing resync).
        var healed = await ChangesAsync(api, book, "not-a-token");
        Assert.Equal(2, healed.Changed.Count);
    }

    [Fact]
    public async Task Inaccessible_collections_are_an_opaque_404()
    {
        var alice = Factory.ApiClient(Email);
        var book = await BookAsync(alice);

        // Another principal addressing Alice's book through their own tree → 404 (unknown or inaccessible).
        var resp = await alice.PostAsJsonAsync($"{Base("mallory@x.test")}/collections/{book}/query", new DavQueryRequest());
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        var anon = Factory.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Base()}/collections")).StatusCode);
    }

    private static async Task<Guid> BookAsync(HttpClient api)
    {
        // The gateway's first act for a principal is the collections listing — which bootstraps.
        var resp = await api.GetAsync($"{Base()}/collections");
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<DavCollectionsDto>();
        return dto!.Collections.Single().Id;
    }

    private static async Task<DavChangesDto> ChangesAsync(HttpClient api, Guid book, string? since)
    {
        var url = $"{Base()}/collections/{book}/changes" + (since is null ? "" : $"?since={Uri.EscapeDataString(since)}");
        var resp = await api.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<DavChangesDto>())!;
    }
}
