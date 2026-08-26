namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's emergency-contact designation; order = priority, empty clears.</summary>
public sealed class SetEmergencyContactsRequest
{
    public required List<Guid> ContactIds { get; set; }
}
