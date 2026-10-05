using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using Marten;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

public sealed class ContactDraftsTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    const string File =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:jane-1\r\nFN:Jane Doe\r\nN:Doe;Jane;;;\r\nTEL;TYPE=cell:+46701\r\nBDAY:--0415\r\nEND:VCARD\r\n" +
        "BEGIN:VCARD\r\nVERSION:2.1\r\nN;CHARSET=UTF-8;ENCODING=QUOTED-PRINTABLE:=C3=96berg;=C3=85sa\r\nEMAIL;HOME:asa@x.test\r\nEND:VCARD\r\n";

    static StringContent Body(string text, string mediaType = "text/vcard") => new(text, Encoding.UTF8, mediaType);

    [Fact]
    public async Task Reads_every_card_into_a_draft()
    {
        var resp = await Factory.ApiClient(Email).PostAsync("/contacts/drafts", Body(File));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var drafts = (await resp.Content.ReadFromJsonAsync<List<ContactDraftDto>>(Json))!;
        Assert.Equal(2, drafts.Count);
        Assert.Equal(("Jane", "Doe", ContactKind.Individual), (drafts[0].GivenName, drafts[0].FamilyName, drafts[0].Kind));
        Assert.Equal(new ContactReachChannel(ReachMedium.Phone, "+46701", "cell", false), Assert.Single(drafts[0].Channels));
        Assert.Equal(new PartialDate(null, 4, 15), drafts[0].Birthday);
        Assert.Equal(("Åsa", "Öberg"), (drafts[1].GivenName, drafts[1].FamilyName));
        Assert.Equal(new ContactReachChannel(ReachMedium.Email, "asa@x.test", "home", false), Assert.Single(drafts[1].Channels));
    }

    [Fact]
    public async Task Creating_from_a_draft_twice_yields_one_contact()
    {
        var api = Factory.ApiClient(Email);
        var bookId = await CreateAddressBookAsync(api);
        var draft = (await (await api.PostAsync("/contacts/drafts", Body(File, "text/plain"))).Content.ReadFromJsonAsync<List<ContactDraftDto>>(Json))![0];
        var request = new CreateContactRequest
        {
            AddressBookId = bookId, SourceKey = draft.SourceKey, Kind = draft.Kind, GivenName = draft.GivenName, FamilyName = draft.FamilyName,
            Channels = draft.Channels, Birthday = draft.Birthday,
        };

        var first = (await (await api.PostAsJsonAsync("/contacts", request, Json)).Content.ReadFromJsonAsync<ContactDto>(Json))!;
        var again = (await (await api.PostAsJsonAsync("/contacts", request, Json)).Content.ReadFromJsonAsync<ContactDto>(Json))!;

        Assert.Equal(first.Id, again.Id);
        await using var session = Store.QuerySession();
        Assert.Equal(1, await session.Query<Contact>().CountAsync());
    }

    [Fact]
    public async Task Nothing_is_saved()
    {
        (await Factory.ApiClient(Email).PostAsync("/contacts/drafts", Body(File))).EnsureSuccessStatusCode();

        await using var session = Store.QuerySession();
        Assert.Equal(0, await session.Query<Contact>().CountAsync());
        Assert.Equal(0, await session.Events.QueryAllRawEvents().CountAsync());
    }

    [Fact]
    public async Task A_malformed_file_is_a_bad_request()
    {
        var resp = await Factory.ApiClient(Email).PostAsync("/contacts/drafts", Body("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Cut off\r\n"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task An_oversized_file_is_refused()
    {
        var resp = await Factory.ApiClient(Email).PostAsync("/contacts/drafts", Body(new string('x', 5 * 1024 * 1024 + 1)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_non_text_body_is_refused()
    {
        var resp = await Factory.ApiClient(Email).PostAsync("/contacts/drafts", Body("{}", "application/json"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_are_rejected()
    {
        var resp = await Factory.AnonymousClient().PostAsync("/contacts/drafts", Body(File));

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
