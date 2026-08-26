namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Wholesale replace from an external sync write (mirrors <see cref="ContactAddressesReplaced"/>).</summary>
public sealed record ContactRelationsReplaced(Guid ContactId, IReadOnlyList<ContactRelation> Relations);
