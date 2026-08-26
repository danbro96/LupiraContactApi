using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.Core.Domain.Contacts.Events;

/// <summary>Upserts one directed relation edge keyed by (ToContactId, Kind); re-adding revives an ended edge.</summary>
public sealed record ContactRelationAdded(Guid ContactId, Guid ToContactId, ContactRelationKind Kind, string? Label, DateOnly? Since = null, string? Note = null);
