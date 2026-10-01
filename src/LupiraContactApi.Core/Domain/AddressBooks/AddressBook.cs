using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;

namespace LupiraContactApi.Core.Domain.AddressBooks;

/// <summary>An address book collection (plain document). Access is via <see cref="AddressBookOwner"/>; it contains <see cref="Contact"/>s and <see cref="ContactGroup"/>s.</summary>
public sealed class AddressBook
{
    /// <summary>The book every principal is bootstrapped with, and where its own contact is created.</summary>
    public const string PersonalSlug = "personal";

    public Guid Id { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
}
