using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;

namespace LupiraContactApi.Core.Domain.AddressBooks;

/// <summary>An address book collection (plain document). Access is via <see cref="AddressBookOwner"/>; it contains <see cref="Contact"/>s and <see cref="ContactGroup"/>s.</summary>
public sealed class AddressBook
{
    /// <summary>The slug of the book every principal is bootstrapped with (see <see cref="PersonalOf"/>).</summary>
    public const string PersonalSlug = "personal";

    public Guid Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    /// <summary>The principal whose personal book this is — its identity, since every member's personal book shares the slug.</summary>
    public Guid? PersonalOf { get; set; }
}
