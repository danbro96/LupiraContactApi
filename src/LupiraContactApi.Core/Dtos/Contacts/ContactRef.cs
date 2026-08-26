namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>A lightweight contact reference (id + display name) — a resolve candidate.</summary>
public sealed class ContactRef
{
    public required Guid ContactId { get; set; }

    public required string DisplayName { get; set; }
}
