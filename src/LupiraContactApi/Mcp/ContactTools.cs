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
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|emergency|other.")] string kind,
        [Description("Optional free-text refinement, e.g. 'dad'.")] string? label = null)
    {
        var u = await user.GetAsync();
        return Require(await contacts.AddRelationAsync(u.Id, contactId, new AddContactRelationRequest { ToContactId = toContactId, Kind = ParseRelationKind(kind), Label = label }));
    }

    [McpServerTool, Description("Remove a contact relation edge by target contact and kind.")]
    public static async Task<ContactDto> unrelate_contacts(
        ContactService contacts, CurrentUser user,
        [Description("The contact the relation is stored on.")] Guid contactId,
        [Description("The related contact.")] Guid toContactId,
        [Description("parent|child|sibling|spouse|partner|friend|colleague|neighbor|emergency|other.")] string kind)
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

    // Strict: a silently-defaulted kind would corrupt the edge.
    private static ContactRelationKind ParseRelationKind(string kind) =>
        Enum.TryParse<ContactRelationKind>(kind, true, out var k) ? k
            : throw new McpException($"Unknown kind '{kind}'. Use parent|child|sibling|spouse|partner|friend|colleague|neighbor|emergency|other.");

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
