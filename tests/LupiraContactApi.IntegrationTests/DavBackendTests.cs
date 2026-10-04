using System.Net;
using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Dav;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>
/// The /dav-backend contract as the LupiraDavApi gateway consumes it: collection listing with JIT
/// provision + personal-book bootstrap, query/multiget, blob round-trip with ETag, PUT/DELETE with
/// preconditions (and the principal's own card refused), and the sync-token changes feed with tombstones.
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
        Assert.Equal(3, all.Count);   // + the caller's own card, created by the bootstrap
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
    public async Task Delete_refuses_the_principals_own_card()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        var listing = await api.PostAsJsonAsync($"{Base()}/collections/{book}/query", new DavQueryRequest());
        var self = Assert.Single((await listing.Content.ReadFromJsonAsync<DavResourcesDto>())!.Resources).Uid;

        Assert.Equal(HttpStatusCode.Forbidden, (await api.DeleteAsync($"{Base()}/collections/{book}/resources/{self}")).StatusCode);
        (await api.GetAsync($"{Base()}/collections/{book}/resources/{self}")).EnsureSuccessStatusCode();
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
        Assert.Equal(3, healed.Changed.Count);   // + the caller's own card
    }

    [Fact]
    public async Task Changes_tombstone_a_resource_moved_to_another_collection()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        var other = await CreateAddressBookAsync(api, "other");
        await PutVcfAsync(api, Email, book, "g@x", MinimalVcf("g@x", "G Seven"));
        var id = (await api.GetFromJsonAsync<List<ContactDto>>($"/contacts?addressBookId={book}"))!.Single(c => c.ExternalId == "g@x").Id;
        var left = await ChangesAsync(api, book, null);
        var joined = await ChangesAsync(api, other, null);

        (await api.PostAsJsonAsync($"/contacts/{id}/move", new MoveContactRequest { AddressBookId = other })).EnsureSuccessStatusCode();

        var leftDiff = await ChangesAsync(api, book, left.SyncToken);
        Assert.Equal(["g@x"], leftDiff.Deleted);
        Assert.Empty(leftDiff.Changed);
        var joinedDiff = await ChangesAsync(api, other, joined.SyncToken);
        Assert.Equal("g@x", Assert.Single(joinedDiff.Changed).Uid);
        Assert.Empty(joinedDiff.Deleted);
        Assert.DoesNotContain((await ChangesAsync(api, book, null)).Changed, c => c.Uid == "g@x");
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

    [Fact]
    public async Task Phone_sync_writes_keep_middle_name_and_nickname()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        var created = await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = book, GivenName = "Jane", MiddleName = "Q", FamilyName = "Doe", Nickname = "Janie",
        });
        var contact = (await created.Content.ReadFromJsonAsync<ContactDto>())!;

        var get = await api.GetAsync($"{Base()}/collections/{book}/resources/{contact.ExternalId}");
        var card = await get.Content.ReadAsStringAsync();
        Assert.Contains("N:Doe;Jane;Q;;\r\n", card);
        Assert.Contains("NICKNAME:Janie\r\n", card);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PutVcfAsync(api, Email, book, contact.ExternalId, card, ifMatch: get.Headers.ETag!.Tag.Trim('"'))).StatusCode);
        var kept = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}"))!;
        Assert.Equal(("Q", "Janie"), (kept.MiddleName, kept.Nickname));

        // A card without NICKNAME leaves the nickname alone; N stays authoritative for the middle name.
        await PutVcfAsync(api, Email, book, contact.ExternalId, card.Replace("NICKNAME:Janie\r\n", "").Replace(";Q;;", ";;;"));
        var unmentioned = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}"))!;
        Assert.Equal((null, "Janie"), (unmentioned.MiddleName, unmentioned.Nickname));

        await PutVcfAsync(api, Email, book, contact.ExternalId, card.Replace("NICKNAME:Janie", "NICKNAME:"));
        Assert.Null((await api.GetFromJsonAsync<ContactDto>($"/contacts/{contact.Id}"))!.Nickname);
    }

    [Fact]
    public async Task Recreating_a_deleted_resource_starts_from_the_card_alone()
    {
        var api = Factory.ApiClient(Email);
        var book = await BookAsync(api);
        var full = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:r@x\r\nFN:Old Name\r\nN:Name;Old;Mid;;\r\nNICKNAME:Oldie\r\n"
            + "EMAIL:old@x.test\r\nNOTE:old note\r\nX-SOCIALPROFILE;TYPE=telegram:oldhandle\r\nEND:VCARD\r\n";
        await PutVcfAsync(api, Email, book, "r@x", full);
        var id = (await api.GetFromJsonAsync<List<ContactDto>>($"/contacts?addressBookId={book}"))!.Single(c => c.ExternalId == "r@x").Id;
        (await api.PutAsJsonAsync($"/contacts/{id}/tags", new SetContactTagsRequest { Tags = ["old"] })).EnsureSuccessStatusCode();
        (await api.DeleteAsync($"{Base()}/collections/{book}/resources/r@x")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Created, (await PutVcfAsync(api, Email, book, "r@x", MinimalVcf("r@x", "Fresh"))).StatusCode);

        var fresh = (await api.GetFromJsonAsync<ContactDto>($"/contacts/{id}"))!;
        Assert.Equal("Fresh", fresh.FamilyName);
        Assert.Null(fresh.MiddleName);
        Assert.Null(fresh.Nickname);
        Assert.Null(fresh.Notes);
        Assert.Null(fresh.Tags);
        Assert.Empty(fresh.Channels);
        Assert.Empty(fresh.Profiles);
    }

    [Fact]
    public async Task Recreating_a_deleted_resource_needs_write_access_to_its_former_collection()
    {
        const string bob = "bob@x.test";
        var alice = Factory.ApiClient(Email);
        await PutVcfAsync(alice, Email, await BookAsync(alice), "shared@x", MinimalVcf("shared@x", "Alice Card"));
        (await alice.DeleteAsync($"{Base()}/collections/{await BookAsync(alice)}/resources/shared@x")).EnsureSuccessStatusCode();

        var bobApi = Factory.ApiClient(bob);
        var bobBook = (await (await bobApi.GetAsync($"{Base(bob)}/collections")).Content.ReadFromJsonAsync<DavCollectionsDto>())!.Collections.Single().Id;
        Assert.Equal(HttpStatusCode.Forbidden, (await PutVcfAsync(bobApi, bob, bobBook, "shared@x", MinimalVcf("shared@x", "Bob Card"))).StatusCode);
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
