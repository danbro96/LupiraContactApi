using LupiraContactApi.Core.Dtos.Contacts;

namespace LupiraContactApi.Core.Dtos.Sync;

/// <summary>A changed contact: the full DTO plus its section guards.</summary>
public sealed class SyncChangeDto
{
    public required ContactDto Contact { get; set; }
    public required SectionGuardsDto Guards { get; set; }
}
