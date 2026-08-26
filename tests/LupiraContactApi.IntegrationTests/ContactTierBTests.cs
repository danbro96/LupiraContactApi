using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>End-to-end coverage (through Marten) for the Tier-B fields: notes, pronouns, a year-less birthday,
/// the avatar pointer, and a relation start date — proving each serializes, replays, and surfaces on the DTO.</summary>
public sealed class ContactTierBTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    [Fact]
    public async Task Create_carries_notes_pronouns_and_a_year_less_birthday()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);

        var resp = await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = abId,
            GivenName = "Iréne",
            FamilyName = "Modig",
            Birthday = new PartialDate(null, 6, 17),   // year unknown
            Notes = "mormor — beekeeping summers",
            Pronouns = "she/her",
        });
        resp.EnsureSuccessStatusCode();
        var c = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;

        Assert.Equal(new PartialDate(null, 6, 17), c.Birthday);
        Assert.Equal("mormor — beekeeping summers", c.Notes);
        Assert.Equal("she/her", c.Pronouns);

        // reload from a fresh read to prove it replayed from the event, not just the write-through
        var reloaded = await api.GetFromJsonAsync<ContactDto>($"/contacts/{c.Id}");
        Assert.Equal(new PartialDate(null, 6, 17), reloaded!.Birthday);
        Assert.Equal("she/her", reloaded.Pronouns);
    }

    [Fact]
    public async Task Revise_merges_notes_without_wiping_the_rest()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, abId, "Jane", "Doe", "jane@x.test");

        var resp = await api.PutAsJsonAsync($"/contacts/{c.Id}", new ReviseContactRequest { Notes = "met at KTH" });
        resp.EnsureSuccessStatusCode();
        var revised = (await resp.Content.ReadFromJsonAsync<ContactDto>())!;

        Assert.Equal("met at KTH", revised.Notes);
        Assert.Contains(revised.Channels, ch => ch.Value == "jane@x.test");   // enrichment never wipes
    }

    [Fact]
    public async Task Avatar_is_set_cleared_and_leaves_the_etag_untouched()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, abId);

        var set = await api.PutAsJsonAsync($"/contacts/{c.Id}/avatar", new SetContactAvatarRequest { AvatarRef = "https://cdn.example/i.jpg" });
        set.EnsureSuccessStatusCode();
        var withAvatar = (await set.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal("https://cdn.example/i.jpg", withAvatar.AvatarRef);
        Assert.Equal(c.Etag, withAvatar.Etag);   // avatar is outside the canonical content

        var clear = await api.PutAsJsonAsync($"/contacts/{c.Id}/avatar", new SetContactAvatarRequest { AvatarRef = "" });
        clear.EnsureSuccessStatusCode();
        Assert.Null((await clear.Content.ReadFromJsonAsync<ContactDto>())!.AvatarRef);
    }

    [Fact]
    public async Task Relation_since_survives_and_shows_on_the_resolved_view()
    {
        var api = Factory.ApiClient(Email);
        var abId = await CreateAddressBookAsync(api);
        var me = await CreateContactAsync(api, abId, "Daniel", "Broström");
        var anton = await CreateContactAsync(api, abId, "Anton", "Alfonsson");

        var resp = await api.PostAsJsonAsync($"/contacts/{me.Id}/relations",
            new AddContactRelationRequest { ToContactId = anton.Id, Kind = ContactRelationKind.Partner, Since = new DateOnly(2019, 11, 1) });
        resp.EnsureSuccessStatusCode();

        var relations = await api.GetFromJsonAsync<List<ContactRelationEntryDto>>($"/contacts/{me.Id}/relations");
        var edge = Assert.Single(relations!, r => r.ContactId == anton.Id && r.Direction == ContactRelationDirection.Outgoing);
        Assert.Equal(new DateOnly(2019, 11, 1), edge.Since);
    }
}
