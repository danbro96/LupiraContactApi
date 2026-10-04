using System.Net.Http.Json;
using Lupira.Contracts.Dav;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Relationships on the sync adapter's cards: each card shows them as seen from its contact, an edit moves the
/// ETag of the card it shows on and lists both contacts as changed, and a card's lines restate them from its side.</summary>
public sealed class DavRelationshipTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private static string Base => $"/dav-backend/u/{Uri.EscapeDataString(Email)}";

    [Fact]
    public async Task Both_cards_show_the_relationship_from_their_own_side()
    {
        var (api, book, child, parent) = await FamilyAsync();
        await RelateAsync(api, child.Id, parent.Id, ContactRelationKind.Parent, "dad");

        Assert.Contains($"RELATED;TYPE=parent;X-LUPIRA-LABEL=dad:urn:uuid:{parent.Id:D}", await CardAsync(api, book, child));
        Assert.Contains($"RELATED;TYPE=child:urn:uuid:{child.Id:D}", await CardAsync(api, book, parent));
    }

    [Fact]
    public async Task An_edit_moves_the_etag_and_lists_both_contacts_as_changed()
    {
        var (api, book, child, parent) = await FamilyAsync();
        await RelateAsync(api, child.Id, parent.Id, ContactRelationKind.Parent);
        var before = await ChangesAsync(api, book, null);
        var etag = await EtagAsync(api, book, parent);

        await RelateAsync(api, parent.Id, child.Id, ContactRelationKind.Child, "kiddo");

        Assert.NotEqual(etag, await EtagAsync(api, book, parent));
        var diff = await ChangesAsync(api, book, before.SyncToken);
        Assert.Contains(diff.Changed, c => c.Uid == child.ExternalId);
        Assert.Contains(diff.Changed, c => c.Uid == parent.ExternalId);
    }

    [Fact]
    public async Task A_card_restates_its_side_and_dropping_a_line_removes_the_relationship()
    {
        var (api, book, child, parent) = await FamilyAsync();
        await RelateAsync(api, child.Id, parent.Id, ContactRelationKind.Parent, "dad");

        var card = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{parent.ExternalId}\r\nFN:Pat Parent\r\nRELATED;TYPE=child;X-LUPIRA-LABEL=kiddo:urn:uuid:{child.Id:D}\r\nEND:VCARD\r\n";
        (await PutVcfAsync(api, Email, book, parent.ExternalId, card)).EnsureSuccessStatusCode();
        Assert.Equal("kiddo", Assert.Single(await RelationsAsync(api, parent.Id)).Label);
        Assert.Equal("dad", Assert.Single(await RelationsAsync(api, child.Id)).Label);   // the other side keeps its word

        var bare = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:{parent.ExternalId}\r\nFN:Pat Parent\r\nRELATED;TYPE=friend:urn:uuid:{Guid.NewGuid():D}\r\nEND:VCARD\r\n";
        (await PutVcfAsync(api, Email, book, parent.ExternalId, bare)).EnsureSuccessStatusCode();
        Assert.Empty(await RelationsAsync(api, child.Id));
    }

    private async Task<(HttpClient Api, Guid Book, ContactDto Child, ContactDto Parent)> FamilyAsync()
    {
        var api = Factory.ApiClient(Email);
        var collections = await api.GetFromJsonAsync<DavCollectionsDto>($"{Base}/collections");   // provisions the personal book
        var book = collections!.Collections.Single().Id;
        return (api, book, await CreateContactAsync(api, book, "Cara", "Child"), await CreateContactAsync(api, book, "Pat", "Parent"));
    }

    private static async Task RelateAsync(HttpClient api, Guid id, Guid toContactId, ContactRelationKind kind, string? label = null) =>
        (await api.PostAsJsonAsync($"/contacts/{id}/relations", new AddContactRelationRequest { ToContactId = toContactId, Kind = kind, Label = label })).EnsureSuccessStatusCode();

    private static async Task<List<ContactRelationEntryDto>> RelationsAsync(HttpClient api, Guid id) =>
        (await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{id}/relations"))!;

    private static Task<string> CardAsync(HttpClient api, Guid book, ContactDto c) =>
        api.GetStringAsync($"{Base}/collections/{book}/resources/{c.ExternalId}");

    private static async Task<string> EtagAsync(HttpClient api, Guid book, ContactDto c)
    {
        var resp = await api.GetAsync($"{Base}/collections/{book}/resources/{c.ExternalId}");
        resp.EnsureSuccessStatusCode();
        return resp.Headers.ETag!.Tag;
    }

    private static async Task<DavChangesDto> ChangesAsync(HttpClient api, Guid book, string? since) =>
        (await api.GetFromJsonAsync<DavChangesDto>($"{Base}/collections/{book}/changes" + (since is null ? "" : $"?since={Uri.EscapeDataString(since)}")))!;
}
