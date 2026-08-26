using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's social/IM handles. For well-known services (telegram, messenger,
/// whatsapp…) the profile URL is derived from the handle when omitted.</summary>
public sealed class SetContactProfilesRequest
{
    public required List<ContactSocialProfileInput> Profiles { get; set; }
    /// <summary>Client wall-clock of the edit, for last-writer-wins conflict resolution. Omitted ⇒ server receive time.</summary>
    public DateTimeOffset? OccurredAt { get; set; }

}
