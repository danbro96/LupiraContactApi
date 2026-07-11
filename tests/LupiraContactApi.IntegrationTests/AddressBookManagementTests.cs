using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.AddressBooks;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Address-book lifecycle beyond create/share: rename (merge), the empty-only delete guard (contacts, groups,
/// and the personal book), and owner-only access-grant listing.</summary>
public sealed class AddressBookManagementTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Alice = "alice@x.test";
    const string Bob = "bob@x.test";

    [Fact]
    public async Task Update_renames_and_merges_display_name()
    {
        var api = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(api, "family", "Family");

        var resp = await api.PutAsJsonAsync($"/address-books/{id}", new UpdateAddressBookRequest { Slug = "kin", DisplayName = "Kinfolk" });
        resp.EnsureSuccessStatusCode();
        var dto = (await resp.Content.ReadFromJsonAsync<AddressBookDto>())!;
        Assert.Equal("kin", dto.Slug);
        Assert.Equal("Kinfolk", dto.DisplayName);

        var keep = await api.PutAsJsonAsync($"/address-books/{id}", new UpdateAddressBookRequest { DisplayName = "The Kin" });   // omitting slug keeps it
        Assert.Equal("kin", (await keep.Content.ReadFromJsonAsync<AddressBookDto>())!.Slug);
    }

    [Fact]
    public async Task Update_is_owner_only()
    {
        var alice = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(alice, "family");
        await alice.PostAsJsonAsync($"/address-books/{id}/owners", new GrantOwnerRequest { Email = Bob, Access = "read-write" });

        var bob = Factory.ApiClient(Bob);
        var attempt = await bob.PutAsJsonAsync($"/address-books/{id}", new UpdateAddressBookRequest { DisplayName = "Hijack" });
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_an_empty_book()
    {
        var api = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(api, "family");

        var del = await api.DeleteAsync($"/address-books/{id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.DoesNotContain((await api.GetFromJsonAsync<List<AddressBookDto>>("/address-books"))!, b => b.Id == id);
    }

    [Fact]
    public async Task Delete_refuses_a_book_that_still_holds_contacts()
    {
        var api = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(api, "family");
        await CreateContactAsync(api, id);

        Assert.Equal(HttpStatusCode.Conflict, (await api.DeleteAsync($"/address-books/{id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_refuses_the_personal_book()
    {
        var api = Factory.ApiClient(Alice);
        var books = await (await api.PostAsync("/me/bootstrap", null)).Content.ReadFromJsonAsync<List<AddressBookDto>>();
        var personal = books!.Single(b => b.Slug == "personal");

        Assert.Equal(HttpStatusCode.Conflict, (await api.DeleteAsync($"/address-books/{personal.Id}")).StatusCode);
    }

    [Fact]
    public async Task List_owners_is_owner_only_and_shows_grants()
    {
        var alice = Factory.ApiClient(Alice);
        var id = await CreateAddressBookAsync(alice, "family");
        await alice.PostAsJsonAsync($"/address-books/{id}/owners", new GrantOwnerRequest { Email = Bob, Access = "read" });

        var owners = await alice.GetFromJsonAsync<List<OwnerGrantDto>>($"/address-books/{id}/owners");
        Assert.Equal(2, owners!.Count);
        Assert.Contains(owners, o => o.Email == Alice && o.Access == Access.Owner);
        Assert.Contains(owners, o => o.Email == Bob && o.Access == Access.Read);

        var bob = Factory.ApiClient(Bob);   // read grant → cannot list
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/address-books/{id}/owners")).StatusCode);
    }
}
