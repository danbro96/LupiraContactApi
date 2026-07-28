using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's reach channels — emails and phones (empty clears). Unlike
/// <c>ReviseContact</c>, which only unions, this can remove a channel. Values are trimmed, type tokens lowercased,
/// duplicates (by medium + value) dropped; at most one preferred channel per medium.</summary>
public sealed class SetContactChannelsRequest
{
    public required List<ContactReachChannel> Channels { get; set; }
    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution. Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

}
