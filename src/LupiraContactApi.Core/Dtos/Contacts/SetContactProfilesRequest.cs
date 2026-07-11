using LupiraContactApi.Domain;

namespace LupiraContactApi.Dtos.Contacts;

/// <summary>Wholesale replacement of a contact's social/IM handles. For well-known services (telegram, messenger,
/// whatsapp…) the profile URL is derived from the handle when omitted.</summary>
public sealed class SetContactProfilesRequest
{
    public required List<ContactSocialProfile> Profiles { get; set; }
}
