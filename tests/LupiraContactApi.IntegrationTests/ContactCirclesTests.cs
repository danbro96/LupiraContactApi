using System.Net;
using System.Net.Http.Json;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Me;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The computed circles read surface: all five buckets over a small world, the self-contact focus fallback
/// via PUT /me/contact, and access scoping of both focus and members.</summary>
public sealed class ContactCirclesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Email = "alice@x.test";

    static async Task RelateAsync(HttpClient api, Guid contactId, Guid toContactId, ContactRelationKind kind) =>
        (await api.PostAsJsonAsync($"/contacts/{contactId}/relations", new AddContactRelationRequest { ToContactId = toContactId, Kind = kind })).EnsureSuccessStatusCode();

    static IReadOnlyList<CircleMemberDto> Members(ContactCirclesDto dto, CircleKind kind) =>
        dto.Circles.Single(c => c.Kind == kind).Members;

    [Fact]
    public async Task All_five_circles_resolve_over_a_small_world()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var me = await CreateContactAsync(api, ab, "Ann", "Me");
        var spouse = await CreateContactAsync(api, ab, "Sam", "Spouse");
        var parent = await CreateContactAsync(api, ab, "Pat", "Parent");
        var grandpa = await CreateContactAsync(api, ab, "Grand", "Pa");
        var friend = await CreateContactAsync(api, ab, "Fred", "Friend");
        var colleague = await CreateContactAsync(api, ab, "Coco", "Colleague");
        var roomie = await CreateContactAsync(api, ab, "Ro", "Roomie");

        await RelateAsync(api, me.Id, spouse.Id, ContactRelationKind.Spouse);
        await RelateAsync(api, me.Id, parent.Id, ContactRelationKind.Parent);
        await RelateAsync(api, parent.Id, grandpa.Id, ContactRelationKind.Parent);
        await RelateAsync(api, me.Id, friend.Id, ContactRelationKind.Friend);

        var org = await api.PostAsJsonAsync($"/address-books/{ab}/groups?kind=organization&name=Acme", new { });
        org.EnsureSuccessStatusCode();
        var orgId = (await org.Content.ReadFromJsonAsync<ContactGroupDto>())!.Id;
        (await api.PostAsJsonAsync($"/groups/{orgId}/members?contactId={me.Id}", new { })).EnsureSuccessStatusCode();
        (await api.PostAsJsonAsync($"/groups/{orgId}/members?contactId={colleague.Id}", new { })).EnsureSuccessStatusCode();

        var home = Guid.NewGuid();
        foreach (var id in new[] { me.Id, roomie.Id })
            (await api.PutAsJsonAsync($"/contacts/{id}/addresses", new SetContactAddressesRequest
            {
                Addresses = [new ContactPostalAddress { PlaceId = home, Type = ContactAddressType.Home }],
            })).EnsureSuccessStatusCode();

        var circles = (await api.GetFromJsonAsync<ContactCirclesDto>($"/contacts/circles?focusId={me.Id}"))!;

        Assert.Equal(me.Id, circles.FocusContactId);
        Assert.Equal(5, circles.Circles.Count);

        var close = Members(circles, CircleKind.CloseFamily);
        Assert.Contains(close, m => m.ContactId == spouse.Id && m.Kind == ContactRelationKind.Spouse && m.Provenance == RelationProvenance.Explicit);
        Assert.Contains(close, m => m.ContactId == parent.Id && m.Kind == ContactRelationKind.Parent);

        var grand = Assert.Single(Members(circles, CircleKind.ExtendedFamily));
        Assert.Equal(grandpa.Id, grand.ContactId);
        Assert.Equal(ContactRelationKind.Grandparent, grand.Kind);
        Assert.Equal(2, grand.Degree);
        Assert.Equal(RelationProvenance.Inferred, grand.Provenance);

        Assert.Equal(friend.Id, Assert.Single(Members(circles, CircleKind.Friends)).ContactId);

        var co = Assert.Single(Members(circles, CircleKind.Colleagues));
        Assert.Equal(colleague.Id, co.ContactId);
        Assert.Equal(RelationProvenance.Inferred, co.Provenance);   // via shared organization

        var household = Assert.Single(Members(circles, CircleKind.Household));
        Assert.Equal(roomie.Id, household.ContactId);
        Assert.Null(household.Kind);
    }

    [Fact]
    public async Task Ended_spouse_leaves_close_family()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var me = await CreateContactAsync(api, ab, "Ann", "Me");
        var ex = await CreateContactAsync(api, ab, "Ed", "Ex");

        await RelateAsync(api, me.Id, ex.Id, ContactRelationKind.Spouse);
        (await api.PostAsJsonAsync($"/contacts/{me.Id}/relations/{ex.Id}/end", new EndContactRelationRequest { Kind = ContactRelationKind.Spouse })).EnsureSuccessStatusCode();

        var circles = (await api.GetFromJsonAsync<ContactCirclesDto>($"/contacts/circles?focusId={me.Id}"))!;
        Assert.Empty(Members(circles, CircleKind.CloseFamily));
    }

    [Fact]
    public async Task Without_a_focus_the_linked_self_contact_is_used_and_unlinked_is_invalid()
    {
        var api = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(api);
        var me = await CreateContactAsync(api, ab, "Ann", "Me");

        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetAsync("/contacts/circles")).StatusCode);

        (await api.PutAsJsonAsync("/me/contact", new SetMyContactRequest { ContactId = me.Id })).EnsureSuccessStatusCode();
        Assert.Equal(me.Id, (await api.GetFromJsonAsync<MeDto>("/me"))!.ContactId);

        var circles = (await api.GetFromJsonAsync<ContactCirclesDto>("/contacts/circles"))!;
        Assert.Equal(me.Id, circles.FocusContactId);
    }

    [Fact]
    public async Task Another_member_cannot_focus_on_an_unreadable_contact()
    {
        var alice = Factory.ApiClient(Email);
        var ab = await CreateAddressBookAsync(alice);
        var me = await CreateContactAsync(alice, ab, "Ann", "Me");

        var bob = Factory.ApiClient("bob@x.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await bob.GetAsync($"/contacts/circles?focusId={me.Id}")).StatusCode);
    }
}
