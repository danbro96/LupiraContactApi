using LupiraContactApi.Application;
using LupiraContactApi.Auth;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.AddressBooks;
using LupiraContactApi.Dtos.Contacts;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace LupiraContactApi.Mcp;

/// <summary>
/// The agent's MCP tool surface, mounted at /mcp. Each tool resolves the caller via <see cref="CurrentUser"/>
/// and delegates to the same Core services as REST, so results are scoped to the member's accessible address books.
/// Non-Ok outcomes surface as a structured <see cref="McpException"/> tool error.
/// </summary>
[McpServerToolType]
public sealed class ContactTools
{
    [McpServerTool, Description("Find contacts the caller can access, optionally by name.")]
    public static async Task<IReadOnlyList<ContactDto>> query_contacts(
        ContactService contacts, CurrentUser user,
        [Description("Free-text query over the contact's name.")] string? query = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.QueryAsync(u.Id, query, null));
    }

    [McpServerTool, Description("Create a contact in an address book (AddressBookId required). Name is structured parts; employer is set separately as an organization group.")]
    public static async Task<ContactDto> create_contact(ContactService contacts, CurrentUser user, CreateContactRequest request)
    {
        var u = await user.GetAsync();
        return Require(await contacts.CreateAsync(u.Id, request));
    }

    [McpServerTool, Description("Relate two contacts: kind is toContactId's role relative to contactId — 'toContactId is contactId's <kind>'. Example: 'X is Y's dad' → relate_contacts(contactId: Y, toContactId: X, kind: 'parent', label: 'dad'). Re-adding the same contact+kind updates the label.")]
    public static async Task<ContactDto> relate_contacts(
        ContactService contacts, CurrentUser user,
        [Description("The contact the relation is stored on.")] Guid contactId,
        [Description("The related contact.")] Guid toContactId,
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|other.")] string kind,
        [Description("Optional free-text refinement, e.g. 'dad'.")] string? label = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.AddRelationAsync(u.Id, contactId, new AddContactRelationRequest { ToContactId = toContactId, Kind = ParseRelationKind(kind), Label = label }));
    }

    [McpServerTool, Description("End a relation (ex-spouse, falling-out): the edge stays, flagged with an optional end date, and no longer asserts current kinship. Use unrelate_contacts only for edges entered by mistake.")]
    public static async Task<ContactDto> end_contact_relation(
        ContactService contacts, CurrentUser user,
        [Description("The contact the relation is stored on.")] Guid contactId,
        [Description("The related contact.")] Guid toContactId,
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|other.")] string kind,
        [Description("When the relationship ended (optional).")] DateOnly? until = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.EndRelationAsync(u.Id, contactId, toContactId, ParseRelationKind(kind), until));
    }

    [McpServerTool, Description("Remove a contact relation edge by target contact and kind — for edges entered by mistake. A relationship that ran its course should be ended via end_contact_relation instead.")]
    public static async Task<ContactDto> unrelate_contacts(
        ContactService contacts, CurrentUser user,
        [Description("The contact the relation is stored on.")] Guid contactId,
        [Description("The related contact.")] Guid toContactId,
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|other.")] string kind)
    {
        var u = await user.GetAsync();
        return Require(await contacts.RemoveRelationAsync(u.Id, contactId, toContactId, ParseRelationKind(kind)));
    }

    [McpServerTool, Description("List a contact's resolved relations, both directions: each entry's kind is the other contact's role relative to this one (incoming edges show the derived inverse, e.g. stored parent → incoming child). Set includeInferred=true to also return kin derived from the parent/child graph (siblings, grandparents/-children, aunts/uncles, cousins, nieces/nephews), each tagged provenance=Inferred.")]
    public static async Task<IReadOnlyList<ContactRelationEntryDto>> list_contact_relations(
        ContactService contacts, CurrentUser user,
        [Description("The contact whose relations to list.")] Guid contactId,
        [Description("Also return kin derived from the parent/child graph, tagged provenance=Inferred.")] bool includeInferred = false)
    {
        var u = await user.GetAsync();
        return Require(await contacts.ListRelationsAsync(u.Id, contactId, includeInferred));
    }

    [McpServerTool, Description("Computed social circles (closeFamily, extendedFamily, friends, colleagues, household) around a focus contact — the caller's own linked contact unless focusId is given. Degree: 1 immediate, 2 two-generation kin, 3 cousin.")]
    public static async Task<ContactCirclesDto> list_contact_circles(
        ContactService contacts, CurrentUser user,
        [Description("Focus contact; defaults to the caller's linked self-contact.")] Guid? focusId = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.CirclesAsync(u.Id, focusId));
    }

    [McpServerTool, Description("Mark a contact as deceased (idempotent; date may be unknown). Deceased contacts stay in the kinship graph — never delete the dead.")]
    public static async Task<ContactDto> mark_contact_deceased(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("Date of death, if known.")] DateOnly? deathDate = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetDeceasedAsync(u.Id, contactId, deathDate));
    }

    [McpServerTool, Description("Undo a deceased marking recorded in error.")]
    public static async Task<ContactDto> clear_contact_deceased(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId)
    {
        var u = await user.GetAsync();
        return Require(await contacts.ClearDeceasedAsync(u.Id, contactId));
    }

    [McpServerTool, Description("Replace a contact's social/IM handles wholesale (telegram, messenger, whatsapp, signal, instagram…). Well-known services get the profile URL derived from the handle; set preferred=true on the handle that actually reaches the person. At most one preferred per service.")]
    public static async Task<ContactDto> set_contact_profiles(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("The full new list — an empty list clears.")] List<ContactSocialProfile> profiles)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetProfilesAsync(u.Id, contactId, profiles));
    }

    [McpServerTool, Description("Replace a contact's postal addresses wholesale; each entry needs a geo place id (LupiraGeoApi) or a formatted address.")]
    public static async Task<ContactDto> set_contact_addresses(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("The full new list — an empty list clears.")] List<ContactPostalAddress> addresses)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetAddressesAsync(u.Id, contactId, addresses));
    }

    [McpServerTool, Description("Replace a contact's emergency-contact designation wholesale (order = priority, empty clears). A designation, not a relation kind.")]
    public static async Task<ContactDto> set_emergency_contacts(
        ContactService contacts, CurrentUser user,
        [Description("The contact.")] Guid contactId,
        [Description("Emergency contact ids in priority order.")] List<Guid> contactIds)
    {
        var u = await user.GetAsync();
        return Require(await contacts.SetEmergencyContactsAsync(u.Id, contactId, contactIds));
    }

    [McpServerTool, Description("Link the caller's identity to its own contact ('this card is me') — the default focus for list_contact_circles.")]
    public static async Task<string> set_my_contact(
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
            : throw new McpException($"Unknown kind '{kind}'. Use parent|child|sibling|spouse|partner|friend|colleague|neighbor|other.");

    [McpServerTool, Description("List the address books the caller can access.")]
    public static async Task<IReadOnlyList<AddressBookDto>> list_address_books(AddressBookService books, CurrentUser user)
    {
        var u = await user.GetAsync();
        return Require(await books.ListAsync(u.Id));
    }

    [McpServerTool, Description("Create an address book (Slug required); the caller becomes its owner.")]
    public static async Task<AddressBookDto> create_address_book(
        AddressBookService books, CurrentUser user,
        [Description("URL-safe short name, e.g. 'family'.")] string slug,
        [Description("Human-readable name.")] string? displayName = null)
    {
        var u = await user.GetAsync();
        return Require(await books.CreateAsync(u.Id, new CreateAddressBookRequest { Slug = slug, DisplayName = displayName }));
    }

    [McpServerTool, Description("Ensure the caller has a personal address book (idempotent); returns all accessible books.")]
    public static async Task<IReadOnlyList<AddressBookDto>> bootstrap_me(AddressBookService books, CurrentUser user)
    {
        var u = await user.GetAsync();
        return Require(await books.BootstrapPersonalAsync(u.Id));
    }

    [McpServerTool, Description("Grant a member access to an address book, by email. access = owner|read-write|read (default owner).")]
    public static async Task<OwnerGrantDto> grant_addressbook_owner(
        AddressBookService books, CurrentUser user,
        [Description("Address book id.")] Guid addressBookId,
        [Description("The member's login email.")] string email,
        [Description("owner|read-write|read.")] string access = "owner")
    {
        var u = await user.GetAsync();
        return Require(await books.GrantOwnerAsync(u.Id, addressBookId, new GrantOwnerRequest { Email = email, Access = access }));
    }

    [McpServerTool, Description("Revoke a member's access to an address book, by email. Fails if it would remove the last owner.")]
    public static async Task<string> revoke_addressbook_owner(
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
