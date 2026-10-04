namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>A deleted contact's content discarded, so a create reusing its key starts afresh instead of reviving it.
/// Relationships are their own aggregate and stay.</summary>
public sealed record ContactPurged(Guid ContactId);
