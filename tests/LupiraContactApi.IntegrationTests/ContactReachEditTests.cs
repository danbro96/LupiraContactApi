using LupiraContactApi.Dtos.AddressBooks;
using LupiraContactApi.Dtos.Contacts;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The removable counterpart to the merge update: PUT /contacts/{id}/emails|phones|tags replaces wholesale, so an
/// entry can be dropped (which the union-only revise cannot). Emails/phones/tags are content-bearing → the ETag moves.</summary>
public sealed class ContactReachEditTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Alice = "alice@x.test";
    const string Bob = "bob@x.test";

    [Fact]
    public async Task Set_emails_replaces_wholesale_can_drop_and_bumps_the_etag()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, email: "jane@x.test");

        var two = await api.PutAsJsonAsync($"/contacts/{c.Id}/emails", new SetContactEmailsRequest { Emails = ["jane@x.test", "jane.doe@work.test"] });
        two.EnsureSuccessStatusCode();
        var afterTwo = (await two.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal(2, afterTwo.Emails!.Length);
        Assert.NotEqual(c.Etag, afterTwo.Etag);   // emails are part of the canonical content

        // Replace with a shorter list — the merge update can only add; this drops one.
        var one = await api.PutAsJsonAsync($"/contacts/{c.Id}/emails", new SetContactEmailsRequest { Emails = ["jane.doe@work.test"] });
        var afterOne = (await one.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.Equal("jane.doe@work.test", Assert.Single(afterOne.Emails!));

        var cleared = await api.PutAsJsonAsync($"/contacts/{c.Id}/emails", new SetContactEmailsRequest { Emails = [] });
        var afterClear = (await cleared.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.True(afterClear.Emails is null || afterClear.Emails.Length == 0);
    }

    [Fact]
    public async Task Set_emails_trims_and_dedupes_case_insensitively()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        var resp = await api.PutAsJsonAsync($"/contacts/{c.Id}/emails", new SetContactEmailsRequest { Emails = ["  Jane@x.test ", "jane@x.test", "   "] });
        resp.EnsureSuccessStatusCode();
        Assert.Equal("Jane@x.test", Assert.Single((await resp.Content.ReadFromJsonAsync<ContactDto>())!.Emails!));   // first casing wins, blanks dropped
    }

    [Fact]
    public async Task Setting_the_same_emails_is_a_noop_and_keeps_the_etag()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab, email: "jane@x.test");

        var resp = await api.PutAsJsonAsync($"/contacts/{c.Id}/emails", new SetContactEmailsRequest { Emails = ["jane@x.test"] });
        resp.EnsureSuccessStatusCode();
        Assert.Equal(c.Etag, (await resp.Content.ReadFromJsonAsync<ContactDto>())!.Etag);   // unchanged → no new event
    }

    [Fact]
    public async Task Set_tags_prunes_where_revise_would_only_union()
    {
        var api = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(api);
        var c = await CreateContactAsync(api, ab);

        await api.PutAsJsonAsync($"/contacts/{c.Id}", new ReviseContactRequest { Tags = ["work", "friend"] });
        var unioned = await api.PutAsJsonAsync($"/contacts/{c.Id}", new ReviseContactRequest { Tags = ["family"] });   // revise unions on
        Assert.Equal(3, (await unioned.Content.ReadFromJsonAsync<ContactDto>())!.Tags!.Length);

        var pruned = await api.PutAsJsonAsync($"/contacts/{c.Id}/tags", new SetContactTagsRequest { Tags = ["family"] });
        Assert.Equal("family", Assert.Single((await pruned.Content.ReadFromJsonAsync<ContactDto>())!.Tags!));

        var cleared = await api.PutAsJsonAsync($"/contacts/{c.Id}/tags", new SetContactTagsRequest { Tags = [] });
        var afterClear = (await cleared.Content.ReadFromJsonAsync<ContactDto>())!;
        Assert.True(afterClear.Tags is null || afterClear.Tags.Length == 0);
    }

    [Fact]
    public async Task Set_phones_requires_write_access()
    {
        var alice = Factory.ApiClient(Alice);
        var ab = await CreateAddressBookAsync(alice);
        var c = await CreateContactAsync(alice, ab);
        await alice.PostAsJsonAsync($"/address-books/{ab}/owners", new GrantOwnerRequest { Email = Bob, Access = "read" });

        var bob = Factory.ApiClient(Bob);
        var attempt = await bob.PutAsJsonAsync($"/contacts/{c.Id}/phones", new SetContactPhonesRequest { Phones = ["+1000"] });
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    }
}
