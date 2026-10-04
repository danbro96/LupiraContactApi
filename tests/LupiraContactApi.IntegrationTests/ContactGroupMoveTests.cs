using System.Net;
using System.Net.Http.Json;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.Sync;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Moving a group between address books keeps its identity and members — membership is by contact id, so it already
/// spans books; optionally the members living in the group's book move with it.</summary>
public sealed class ContactGroupMoveTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    const string Alice = "alice@x.test";
    const string Bob = "bob@x.test";

    static Task<HttpResponseMessage> MoveAsync(HttpClient api, Guid groupId, Guid addressBookId, bool includeMembers = false) =>
        api.PostAsJsonAsync($"/groups/{groupId}/move", new MoveContactGroupRequest { AddressBookId = addressBookId, IncludeMembers = includeMembers });

    static async Task<ContactGroupDto> CreateGroupAsync(HttpClient api, Guid addressBookId, params (Guid ContactId, string? Role)[] members)
    {
        var resp = await api.PostAsync($"/address-books/{addressBookId}/groups?kind=organization&name=Acme", null);
        resp.EnsureSuccessStatusCode();
        var group = (await resp.Content.ReadFromJsonAsync<ContactGroupDto>())!;
        foreach (var (contactId, role) in members)
            (await api.PostAsync($"/groups/{group.Id}/members?contactId={contactId}" + (role is null ? "" : $"&role={role}"), null)).EnsureSuccessStatusCode();
        return group;
    }

    [Fact]
    public async Task Move_keeps_members_and_roles_and_reaches_the_target_books_readers()
    {
        var alice = Factory.ApiClient(Alice);
        var reader = Factory.ApiClient(Bob);
        var from = await CreateAddressBookAsync(alice, "from");
        var to = await CreateAddressBookAsync(alice, "to");
        await GrantAsync(alice, to, Bob, "read");
        var member = await CreateContactAsync(alice, from);
        var group = await CreateGroupAsync(alice, from, (member.Id, "Engineer"));
        Assert.Empty((await reader.GetFromJsonAsync<SyncContainersResponse>("/sync/containers"))!.Groups);

        var resp = await MoveAsync(alice, group.Id, to);
        resp.EnsureSuccessStatusCode();
        var moved = (await resp.Content.ReadFromJsonAsync<MoveContactGroupResponse>())!;

        Assert.Equal((group.Id, to, group.Name, group.Kind), (moved.Group.Id, moved.Group.AddressBookId, moved.Group.Name, moved.Group.Kind));
        Assert.Equal((0, 0), (moved.MembersMoved, moved.MembersSkipped));
        Assert.DoesNotContain((await alice.GetFromJsonAsync<List<ContactGroupDto>>($"/address-books/{from}/groups"))!, g => g.Id == group.Id);
        var seen = Assert.Single((await reader.GetFromJsonAsync<SyncContainersResponse>("/sync/containers"))!.Groups);
        var m = Assert.Single(seen.Members);
        Assert.Equal((group.Id, member.Id, "Engineer"), (seen.Id, m.ContactId, m.Role));
        Assert.Equal(from, (await alice.GetFromJsonAsync<ContactDto>($"/contacts/{member.Id}"))!.AddressBookId);   // members stay put
    }

    [Fact]
    public async Task Including_members_moves_only_those_living_in_the_groups_book()
    {
        var alice = Factory.ApiClient(Alice);
        var from = await CreateAddressBookAsync(alice, "from");
        var to = await CreateAddressBookAsync(alice, "to");
        var elsewhere = await CreateAddressBookAsync(alice, "elsewhere");
        var local = await CreateContactAsync(alice, from, "Local");
        var outside = await CreateContactAsync(alice, elsewhere, "Outside");
        var group = await CreateGroupAsync(alice, from, (local.Id, "Lead"), (outside.Id, null));

        var resp = await MoveAsync(alice, group.Id, to, includeMembers: true);
        resp.EnsureSuccessStatusCode();
        var moved = (await resp.Content.ReadFromJsonAsync<MoveContactGroupResponse>())!;

        Assert.Equal((1, 1), (moved.MembersMoved, moved.MembersSkipped));
        Assert.Equal(2, moved.Group.Members.Count);
        Assert.Equal(to, (await alice.GetFromJsonAsync<ContactDto>($"/contacts/{local.Id}"))!.AddressBookId);
        Assert.Equal(elsewhere, (await alice.GetFromJsonAsync<ContactDto>($"/contacts/{outside.Id}"))!.AddressBookId);
    }

    [Fact]
    public async Task Mcp_group_move_lists_each_members_outcome()
    {
        var alice = Factory.ApiClient(Alice);
        var from = await CreateAddressBookAsync(alice, "from");
        var to = await CreateAddressBookAsync(alice, "to");
        var local = await CreateContactAsync(alice, from, "Local");
        var outside = await CreateContactAsync(alice, await CreateAddressBookAsync(alice, "elsewhere"), "Outside");
        var gone = await CreateContactAsync(alice, from, "Gone");
        var group = await CreateGroupAsync(alice, from, (local.Id, null), (outside.Id, null), (gone.Id, null));
        (await alice.DeleteAsync($"/contacts/{gone.Id}")).EnsureSuccessStatusCode();

        await using var mcp = await McpAsync(Alice);
        var result = ToolResult<ContactGroupMoveResult>(await mcp.CallToolAsync("move_contact_group", new Dictionary<string, object?>
        {
            ["groupId"] = group.Id,
            ["addressBookId"] = to,
            ["includeMembers"] = true,
        }));

        Assert.Equal(to, result.Group.AddressBookId);
        Assert.Equal(
            [(local.Id, ContactMoveOutcome.Moved), (outside.Id, ContactMoveOutcome.Skipped), (gone.Id, ContactMoveOutcome.NotFound)],
            result.Members.Select(o => (o.ContactId, o.Outcome)));
    }

    [Fact]
    public async Task Move_needs_write_access_on_both_books_and_404s_the_unknown()
    {
        var alice = Factory.ApiClient(Alice);
        var bob = Factory.ApiClient(Bob);
        var alicesBook = await CreateAddressBookAsync(alice, "alices");
        var bobsBook = await CreateAddressBookAsync(bob, "bobs");
        var group = await CreateGroupAsync(alice, alicesBook);
        await GrantAsync(alice, alicesBook, Bob, "read");

        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(bob, group.Id, bobsBook)).StatusCode);   // source read-only
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(alice, group.Id, bobsBook)).StatusCode);   // target unshared
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(alice, Guid.NewGuid(), alicesBook)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await MoveAsync(alice, group.Id, Guid.NewGuid())).StatusCode);

        var noop = await MoveAsync(alice, group.Id, alicesBook);
        noop.EnsureSuccessStatusCode();
        Assert.Equal(alicesBook, (await noop.Content.ReadFromJsonAsync<MoveContactGroupResponse>())!.Group.AddressBookId);
    }
}
