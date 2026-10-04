using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The member-facing birthdays feed behind cal-api's Birthdays calendar, scoped to the caller's
/// readable address books.</summary>
public sealed class ContactBirthdaysTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Alice = "alice@x.test";
    private const string Bob = "bob@x.test";

    private static async Task<ContactDto> CreateWithBirthdayAsync(HttpClient api, Guid book, string given, PartialDate birthday) =>
        (await (await api.PostAsJsonAsync("/contacts", new CreateContactRequest
        {
            AddressBookId = book,
            GivenName = given,
            FamilyName = "Test",
            Birthday = birthday,
        })).EnsureSuccessStatusCode().Content.ReadFromJsonAsync<ContactDto>())!;

    [Fact]
    public async Task Lists_dated_and_yearless_and_omits_deceased_deleted_and_birthdayless()
    {
        var alice = Factory.ApiClient(Alice);
        var book = await CreateAddressBookAsync(alice);
        var dated = await CreateWithBirthdayAsync(alice, book, "Ada", new PartialDate(1815, 12, 10));
        var yearless = await CreateWithBirthdayAsync(alice, book, "Grace", new PartialDate(null, 12, 9));
        var deceased = await CreateWithBirthdayAsync(alice, book, "Alan", new PartialDate(1912, 6, 23));
        (await alice.PutAsJsonAsync($"/contacts/{deceased.Id}/deceased", new SetDeceasedRequest { DeathDate = null })).EnsureSuccessStatusCode();
        var deleted = await CreateWithBirthdayAsync(alice, book, "Gone", new PartialDate(1990, 1, 1));
        (await alice.DeleteAsync($"/contacts/{deleted.Id}")).EnsureSuccessStatusCode();
        _ = await CreateContactAsync(alice, book, "No", "Birthday");

        var result = (await alice.GetFromJsonAsync<List<ContactBirthdayDto>>("/contacts/birthdays"))!;

        Assert.Equal([yearless.Id, dated.Id], result.Select(b => b.ContactId));
        var ada = result.Single(b => b.ContactId == dated.Id);
        Assert.Equal((1815, 12, 10), (ada.Year, ada.Month, ada.Day));
        Assert.Null(result.Single(b => b.ContactId == yearless.Id).Year);
    }

    [Fact]
    public async Task Is_scoped_to_the_callers_readable_books()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var bobBook = await CreateAddressBookAsync(bob, "bobs");
        var bobsFriend = await CreateWithBirthdayAsync(bob, bobBook, "Carol", new PartialDate(1980, 3, 3));

        Assert.Empty((await alice.GetFromJsonAsync<List<ContactBirthdayDto>>("/contacts/birthdays"))!);

        (await bob.PostAsJsonAsync($"/address-books/{bobBook}/owners", new GrantOwnerRequest { Email = Alice, Access = "read" }))
            .EnsureSuccessStatusCode();

        var shared = (await alice.GetFromJsonAsync<List<ContactBirthdayDto>>("/contacts/birthdays"))!;
        Assert.Equal(bobsFriend.Id, Assert.Single(shared).ContactId);
    }

    [Fact]
    public async Task Service_identity_without_books_sees_nothing()
    {
        var alice = Factory.ApiClient(Alice);
        var book = await CreateAddressBookAsync(alice);
        _ = await CreateWithBirthdayAsync(alice, book, "Ada", new PartialDate(1815, 12, 10));

        Assert.Empty((await Factory.ScopedClient("svc@x.test", "internal:read").GetFromJsonAsync<List<ContactBirthdayDto>>("/contacts/birthdays"))!);
    }

    [Fact]
    public async Task Requires_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Factory.AnonymousClient().GetAsync("/contacts/birthdays")).StatusCode);
    }
}
