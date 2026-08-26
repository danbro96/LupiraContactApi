using System.ComponentModel;
using LupiraContactApi.Auth;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Application.Results;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LupiraContactApi.Mcp;

/// <summary>
/// The agent's MCP tool surface, mounted at /mcp. Each tool resolves the caller via <see cref="CurrentUser"/>
/// and delegates to the same Core services as REST, so results are scoped to the member's accessible address books.
/// Non-Ok outcomes surface as a structured <see cref="McpException"/> tool error.
/// </summary>
[McpServerToolType]
public sealed class ContactTools
{
    [McpServerTool(Name = "query_contacts")]
    [Description("Find contacts the caller can access, optionally by name.")]
    public static async Task<IReadOnlyList<ContactDto>> QueryContacts(
        ContactService contacts, CurrentUser user,
        [Description("Free-text query over the contact's name.")] string? query = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.QueryAsync(u.Id, query, null));
    }

    [McpServerTool(Name = "list_thin_contacts")]
    [Description("Check-in worklist: contacts ranked thinnest-first by completeness score (0..1 ascending). Kind-aware: organisation/venue cards are only scored on name/reach/address; the deceased on remembrance data. Each contact carries Completeness with ranked Gaps — the fields worth asking about. When a gap doesn't apply (grandma has no employer), acknowledge it via attach_metadata with {\"completeness\":{\"na\":[\"organisation\"]}} so it stops counting.")]
    public static async Task<IReadOnlyList<ContactDto>> ListThinContacts(
        ContactService contacts, CurrentUser user,
        [Description("Restrict to one address book id.")] Guid? addressBookId = null,
        [Description("Only contacts scoring strictly below this (0..1). Default 1 = any contact with gaps.")] double? maxScore = null,
        [Description("Max contacts returned (default 25).")] int? take = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.ThinContactsAsync(u.Id, addressBookId, maxScore, take));
    }

    [McpServerTool(Name = "attach_metadata")]
    [Description("Merge an arbitrary JSON object of metadata into a contact (top-level keys overwrite). Also the channel for completeness N/A acknowledgments: {\"completeness\":{\"na\":[\"organisation\",\"birthday\"]}} marks those rubric fields as not applicable so the contact's completeness score stops counting them.")]
    public static async Task<ContactDto> AttachMetadata(
        ContactService contacts, CurrentUser user,
        [Description("The contact id.")] Guid contactId,
        [Description("A JSON object of metadata keys to merge.")] string metadataJson)
    {
        var u = await user.GetAsync();
        var node = System.Text.Json.Nodes.JsonNode.Parse(metadataJson) ?? new System.Text.Json.Nodes.JsonObject();
        return Require(await contacts.AttachMetadataAsync(u.Id, contactId, node));
    }

    [McpServerTool(Name = "create_contact")]
    [Description("Create a contact in an address book (AddressBookId required). Kind = Individual (default) or Organization — use Organization for a business/venue card (a restaurant, clinic, airline); it skips person-only completeness asks. Name is structured parts; employer is set separately as an organization group. Notes/pronouns and a year-optional birthday can be set here.")]
    public static async Task<ContactDto> CreateContact(ContactService contacts, CurrentUser user, CreateContactRequest request)
    {
        var u = await user.GetAsync();
        return Require(await contacts.CreateAsync(u.Id, request));
    }

    [McpServerTool(Name = "create_contacts_batch")]
    [Description("Create many contacts in one call (each item carries its AddressBookId). Returns them in input order. Use for imports instead of repeated create_contact. Max 100; the whole batch fails on any forbidden book or channel conflict.")]
    public static async Task<IReadOnlyList<ContactDto>> CreateContactsBatch(ContactService contacts, CurrentUser user, CreateContactsBatchRequest request)
    {
        var u = await user.GetAsync();
        return Require(await contacts.CreateBatchAsync(u.Id, request.Contacts));
    }

    [McpServerTool(Name = "resolve_contacts")]
    [Description("Batch-match a list of names to existing contacts for import disambiguation. Per name: outcome Matched (one normalized-name/substring hit → contactId), Ambiguous (several → see candidates), or NotFound; candidates are id+displayName. Substring + normalized-name match, not phonetic. Optionally scope to one AddressBookId.")]
    public static async Task<IReadOnlyList<ContactNameMatch>> ResolveContacts(ContactService contacts, CurrentUser user, ResolveContactsByNameRequest request)
    {
        var u = await user.GetAsync();
        return Require(await contacts.ResolveByNameAsync(u.Id, request.Names, request.AddressBookId));
    }

    [McpServerTool(Name = "get_contact")]
    [Description("Fetch one contact by id (query_contacts searches by name).")]
    public static async Task<ContactDto> GetContact(ContactService contacts, CurrentUser user, [Description("The contact id.")] Guid contactId)
    {
        var u = await user.GetAsync();
        return Require(await contacts.GetAsync(u.Id, contactId));
    }

    [McpServerTool(Name = "revise_contact")]
    [Description("Merge-update a contact: provided scalars overwrite, provided channels/tags union onto the existing, null fields are kept (never wipes what it didn't mention). Use set_contact_channels/set_contact_tags to remove.")]
    public static async Task<ContactDto> ReviseContact(ContactService contacts, CurrentUser user, [Description("The contact id.")] Guid contactId, ReviseContactRequest request)
    {
        var u = await user.GetAsync();
        return Require(await contacts.ReviseAsync(u.Id, contactId, request));
    }

    [McpServerTool(Name = "delete_contact")]
    [Description("Soft-delete a contact (tombstoned; a subsequent create/import with the same uid resurrects it). Do not delete the dead — mark_contact_deceased keeps them in the graph.")]
    public static async Task<string> DeleteContact(ContactService contacts, CurrentUser user, [Description("The contact id.")] Guid contactId)
    {
        var u = await user.GetAsync();
        Require(await contacts.DeleteAsync(u.Id, contactId));
        return $"Deleted contact {contactId}.";
    }

    [McpServerTool(Name = "relate_contacts")]
    [Description("Relate two contacts: kind is toContactId's role relative to contactId — 'toContactId is contactId's <kind>'. Example: 'X is Y's dad' → relate_contacts(contactId: Y, toContactId: X, kind: 'parent', label: 'dad'). Re-adding the same contact+kind updates the label.")]
    public static async Task<ContactDto> RelateContacts(
        ContactService contacts, CurrentUser user,
        [Description("The contact the relation is stored on.")] Guid contactId,
        [Description("The related contact.")] Guid toContactId,
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|other|grandparent|grandchild|auntuncle|niecenephew|cousin.")] string kind,
        [Description("Optional free-text refinement, e.g. 'dad'.")] string? label = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.AddRelationAsync(u.Id, contactId, new AddContactRelationRequest { ToContactId = toContactId, Kind = ParseRelationKind(kind), Label = label }));
    }

    [McpServerTool(Name = "end_contact_relation")]
    [Description("End a relation (ex-spouse, falling-out): the edge stays, flagged with an optional end date, and no longer asserts current kinship. Use unrelate_contacts only for edges entered by mistake.")]
    public static async Task<ContactDto> EndContactRelation(
        ContactService contacts, CurrentUser user,
        [Description("The contact the relation is stored on.")] Guid contactId,
        [Description("The related contact.")] Guid toContactId,
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|other|grandparent|grandchild|auntuncle|niecenephew|cousin.")] string kind,
        [Description("When the relationship ended (optional).")] DateOnly? until = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.EndRelationAsync(u.Id, contactId, toContactId, ParseRelationKind(kind), until));
    }

    [McpServerTool(Name = "unrelate_contacts")]
    [Description("Remove a contact relation edge by target contact and kind — for edges entered by mistake. A relationship that ran its course should be ended via end_contact_relation instead.")]
    public static async Task<ContactDto> UnrelateContacts(
        ContactService contacts, CurrentUser user,
        [Description("The contact the relation is stored on.")] Guid contactId,
        [Description("The related contact.")] Guid toContactId,
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|other|grandparent|grandchild|auntuncle|niecenephew|cousin.")] string kind)
    {
        var u = await user.GetAsync();
        return Require(await contacts.RemoveRelationAsync(u.Id, contactId, toContactId, ParseRelationKind(kind)));
    }

    [McpServerTool(Name = "list_contact_relations")]
    [Description("List a contact's resolved relations, both directions: each entry's kind is the other contact's role relative to this one (incoming edges show the derived inverse, e.g. stored parent → incoming child). Set includeInferred=true to also return kin derived from the parent/child graph (siblings, grandparents/-children, aunts/uncles, cousins, nieces/nephews), each tagged provenance=Inferred.")]
    public static async Task<IReadOnlyList<ContactRelationEntryDto>> ListContactRelations(
        ContactService contacts, CurrentUser user,
        [Description("The contact whose relations to list.")] Guid contactId,
        [Description("Also return kin derived from the parent/child graph, tagged provenance=Inferred.")] bool includeInferred = false)
    {
        var u = await user.GetAsync();
        return Require(await contacts.ListRelationsAsync(u.Id, contactId, includeInferred));
    }

    [McpServerTool(Name = "list_contact_circles")]
    [Description("Computed social circles (closeFamily, extendedFamily, friends, colleagues, household) around a focus contact — the caller's own linked contact unless focusId is given. Degree: 1 immediate, 2 two-generation kin, 3 cousin.")]
    public static async Task<ContactCirclesDto> ListContactCircles(
        ContactService contacts, CurrentUser user,
        [Description("Focus contact; defaults to the caller's linked self-contact.")] Guid? focusId = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.CirclesAsync(u.Id, focusId));
    }

    [McpServerTool(Name = "mark_contact_deceased")]
    [Description("Mark a contact as deceased (idempotent; date may be unknown). Deceased contacts stay in the kinship graph — never delete the dead.")]
    public static async Task<ContactDto> MarkContactDeceased(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("Date of death, if known.")] DateOnly? deathDate = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetDeceasedAsync(u.Id, contactId, deathDate));
    }

    [McpServerTool(Name = "clear_contact_deceased")]
    [Description("Undo a deceased marking recorded in error.")]
    public static async Task<ContactDto> ClearContactDeceased(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId)
    {
        var u = await user.GetAsync();
        return Require(await contacts.ClearDeceasedAsync(u.Id, contactId));
    }

    [McpServerTool(Name = "set_contact_profiles")]
    [Description("Replace a contact's social/IM handles wholesale (telegram, messenger, whatsapp, signal, instagram…). Well-known services get the profile URL derived from the handle; set preferred=true on the handle that actually reaches the person. At most one preferred per service.")]
    public static async Task<ContactDto> SetContactProfiles(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("The full new list — an empty list clears.")] List<ContactSocialProfileInput> profiles)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetProfilesAsync(u.Id, contactId, profiles));
    }

    [McpServerTool(Name = "set_contact_addresses")]
    [Description("Replace a contact's postal addresses wholesale; each entry needs a LupiraGeoApi place id (resolve the address there first — no free-text). Empty clears. Optional movedIn/movedOut fuzzy dates ({year, month?, day?}); active = today inside the period, so a past movedOut is former and future dates are planned.")]
    public static async Task<ContactDto> SetContactAddresses(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("The full new list — an empty list clears. Past movedOut = former; future dates = planned.")] List<ContactPostalAddress> addresses)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetAddressesAsync(u.Id, contactId, addresses));
    }

    [McpServerTool(Name = "set_contact_channels")]
    [Description("Replace a contact's reach channels (emails + phones) wholesale (empty clears). Each channel: medium=email|phone, value, optional type (home|work|cell|fax|…), preferred. At most one preferred per medium.")]
    public static async Task<ContactDto> SetContactChannels(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("The full new list — an empty list clears.")] List<ContactReachChannel> channels)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetChannelsAsync(u.Id, contactId, channels));
    }

    [McpServerTool(Name = "set_emergency_contacts")]
    [Description("Replace a contact's emergency-contact designation wholesale (order = priority, empty clears). A designation, not a relation kind.")]
    public static async Task<ContactDto> SetEmergencyContacts(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("Emergency contact ids in priority order.")] List<Guid> contactIds)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetEmergencyContactsAsync(u.Id, contactId, contactIds));
    }

    [McpServerTool(Name = "set_contact_tags")]
    [Description("Replace a contact's tags wholesale (empty clears). Tags are trimmed and de-duplicated case-insensitively; order is preserved.")]
    public static async Task<ContactDto> SetContactTags(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("The full new tag list — an empty list clears.")] string[] tags)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetTagsAsync(u.Id, contactId, tags));
    }

    [McpServerTool(Name = "set_contact_avatar")]
    [Description("Set (or clear, with an empty value) a contact's avatar — a URL/media id, never image bytes.")]
    public static async Task<ContactDto> SetContactAvatar(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("Avatar URL/media id; empty clears.")] string? avatarRef = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetAvatarAsync(u.Id, contactId, avatarRef));
    }

    [McpServerTool(Name = "set_my_contact")]
    [Description("Link the caller's identity to its own contact ('this card is me') — the default focus for list_contact_circles.")]
    public static async Task<string> SetMyContact(
        ContactService contacts, CurrentUser user,
        [Description("The caller's own contact.")] Guid contactId)
    {
        var u = await user.GetAsync();
        Require(await contacts.LinkSelfContactAsync(u.Id, contactId));
        return $"Linked contact {contactId} as your self-contact.";
    }

    // Strict: a silently-defaulted kind would corrupt the edge.
    private static ContactRelationKind ParseRelationKind(string kind) =>
        Enum.TryParse<ContactRelationKind>(kind, true, out var k) ? k
            : throw new McpException($"Unknown kind '{kind}'. Use parent|child|sibling|spouse|partner|friend|colleague|neighbor|other|grandparent|grandchild|auntuncle|niecenephew|cousin.");

    [McpServerTool(Name = "list_address_books")]
    [Description("List the address books the caller can access.")]
    public static async Task<IReadOnlyList<AddressBookDto>> ListAddressBooks(AddressBookService books, CurrentUser user)
    {
        var u = await user.GetAsync();
        return Require(await books.ListAsync(u.Id));
    }

    [McpServerTool(Name = "create_address_book")]
    [Description("Create an address book (Slug required); the caller becomes its owner.")]
    public static async Task<AddressBookDto> CreateAddressBook(
        AddressBookService books, CurrentUser user,
        [Description("URL-safe short name, e.g. 'family'.")] string slug,
        [Description("Human-readable name.")] string? displayName = null)
    {
        var u = await user.GetAsync();
        return Require(await books.CreateAsync(u.Id, new CreateAddressBookRequest { Slug = slug, DisplayName = displayName }));
    }

    [McpServerTool(Name = "bootstrap_me")]
    [Description("Ensure the caller has a personal address book (idempotent); returns all accessible books.")]
    public static async Task<IReadOnlyList<AddressBookDto>> BootstrapMe(AddressBookService books, CurrentUser user)
    {
        var u = await user.GetAsync();
        return Require(await books.BootstrapPersonalAsync(u.Id));
    }

    // ---- Contact groups (personal groupings + organizations) ----

    [McpServerTool(Name = "list_contact_groups")]
    [Description("List the contact groups (personal groupings + organizations) in an address book.")]
    public static async Task<IReadOnlyList<ContactGroupDto>> ListContactGroups(
        ContactGroupService groups, CurrentUser user, [Description("Address book id.")] Guid addressBookId)
    {
        var u = await user.GetAsync();
        return Require(await groups.ListAsync(u.Id, addressBookId));
    }

    [McpServerTool(Name = "create_contact_group")]
    [Description("Create a contact group. kind = group|organization — an employer is an organization-kind group, and the Colleagues circle derives from shared organization membership.")]
    public static async Task<ContactGroupDto> CreateContactGroup(
        ContactGroupService groups, CurrentUser user,
        [Description("Address book id.")] Guid addressBookId,
        [Description("Group name, e.g. 'Firefly'.")] string name,
        [Description("group|organization (default group).")] string kind = "group")
    {
        var u = await user.GetAsync();
        return Require(await groups.CreateAsync(u.Id, addressBookId, kind, name));
    }

    [McpServerTool(Name = "rename_contact_group")]
    [Description("Rename a contact group.")]
    public static async Task<ContactGroupDto> RenameContactGroup(
        ContactGroupService groups, CurrentUser user, [Description("Group id.")] Guid groupId, [Description("New name.")] string name)
    {
        var u = await user.GetAsync();
        return Require(await groups.RenameAsync(u.Id, groupId, name));
    }

    [McpServerTool(Name = "add_group_member")]
    [Description("Add a contact to a group (re-adding updates the details). For an organization, role is the title held there and since/until bound the tenure — a person can hold several jobs via several memberships.")]
    public static async Task<ContactGroupDto> AddGroupMember(
        ContactGroupService groups, CurrentUser user,
        [Description("Group id.")] Guid groupId,
        [Description("Contact to add.")] Guid contactId,
        [Description("Title/role held in an organization (optional).")] string? role = null,
        [Description("When the membership began (optional).")] DateOnly? since = null,
        [Description("When the membership ended (optional).")] DateOnly? until = null)
    {
        var u = await user.GetAsync();
        return Require(await groups.AddMemberAsync(u.Id, groupId, contactId, role, since, until));
    }

    [McpServerTool(Name = "remove_group_member")]
    [Description("Remove a contact from a group.")]
    public static async Task<ContactGroupDto> RemoveGroupMember(
        ContactGroupService groups, CurrentUser user, [Description("Group id.")] Guid groupId, [Description("Contact to remove.")] Guid contactId)
    {
        var u = await user.GetAsync();
        return Require(await groups.RemoveMemberAsync(u.Id, groupId, contactId));
    }

    [McpServerTool(Name = "delete_contact_group")]
    [Description("Delete a contact group.")]
    public static async Task<string> DeleteContactGroup(
        ContactGroupService groups, CurrentUser user, [Description("Group id.")] Guid groupId)
    {
        var u = await user.GetAsync();
        Require(await groups.DeleteAsync(u.Id, groupId));
        return $"Deleted group {groupId}.";
    }

    [McpServerTool(Name = "grant_addressbook_owner")]
    [Description("Grant a member access to an address book, by email. access = owner|read-write|read (default owner).")]
    public static async Task<OwnerGrantDto> GrantAddressbookOwner(
        AddressBookService books, CurrentUser user,
        [Description("Address book id.")] Guid addressBookId,
        [Description("The member's login email.")] string email,
        [Description("owner|read-write|read.")] string access = "owner")
    {
        var u = await user.GetAsync();
        return Require(await books.GrantOwnerAsync(u.Id, addressBookId, new GrantOwnerRequest { Email = email, Access = access }));
    }

    [McpServerTool(Name = "revoke_addressbook_owner")]
    [Description("Revoke a member's access to an address book, by email. Fails if it would remove the last owner.")]
    public static async Task<string> RevokeAddressbookOwner(
        AddressBookService books, CurrentUser user,
        [Description("Address book id.")] Guid addressBookId,
        [Description("The member's login email.")] string email)
    {
        var u = await user.GetAsync();
        Require(await books.RevokeOwnerAsync(u.Id, addressBookId, email));
        return $"Revoked {email}'s access to address book {addressBookId}.";
    }

    /// <summary>Unwraps a service outcome to its value, surfacing non-Ok statuses as an MCP tool error.</summary>
    private static T Require<T>(OpResult<T> r) => r.Status switch
    {
        OpStatus.Ok => r.Value!,
        OpStatus.NotFound => throw new McpException("Not found."),
        OpStatus.Forbidden => throw new McpException(r.Error ?? "Forbidden."),
        OpStatus.Invalid => throw new McpException(r.Error ?? "Invalid request."),
        OpStatus.Conflict => throw new McpException(r.Error ?? "Conflict."),
        _ => throw new McpException("Unexpected result."),
    };

    /// <summary>Asserts a no-content outcome succeeded, surfacing non-Ok statuses as an MCP tool error.</summary>
    private static void Require(OpResult r)
    {
        if (r.IsOk) return;
        throw r.Status switch
        {
            OpStatus.NotFound => new McpException("Not found."),
            OpStatus.Forbidden => new McpException(r.Error ?? "Forbidden."),
            OpStatus.Invalid => new McpException(r.Error ?? "Invalid request."),
            OpStatus.Conflict => new McpException(r.Error ?? "Conflict."),
            _ => new McpException("Unexpected result."),
        };
    }
}
