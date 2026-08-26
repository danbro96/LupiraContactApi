using LupiraContactApi.Core.Domain.Contacts;

namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>Per-section guards for a contact. <c>Core</c> covers the whole ContactFields (name/channels/tags/
/// notes… — channel and tag writes ride the same revision event); the rest map 1:1 to their write endpoints.</summary>
public sealed class SectionGuardsDto
{
    public required SectionGuardDto Core { get; set; }
    public required SectionGuardDto Addresses { get; set; }
    public required SectionGuardDto Profiles { get; set; }
    public required SectionGuardDto Avatar { get; set; }
    public required SectionGuardDto Metadata { get; set; }
    public required SectionGuardDto Deceased { get; set; }

    internal static SectionGuardsDto From(Contact c) => new()
    {
        Core = SectionGuardDto.From(c.CoreTs, c.CoreCmd),
        Addresses = SectionGuardDto.From(c.AddressesTs, c.AddressesCmd),
        Profiles = SectionGuardDto.From(c.ProfilesTs, c.ProfilesCmd),
        Avatar = SectionGuardDto.From(c.AvatarTs, c.AvatarCmd),
        Metadata = SectionGuardDto.From(c.MetadataTs, c.MetadataCmd),
        Deceased = SectionGuardDto.From(c.DeceasedTs, c.DeceasedCmd),
    };
}
