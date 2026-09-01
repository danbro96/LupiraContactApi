namespace LupiraContactApi.Core.Dtos.Internal;

/// <summary>Descriptor material for one contact: identity plus the free-text fields a sibling
/// service condenses into its own summary. Relations are pre-rendered lines
/// (<c>"partner: Anton Alfonsson"</c>) — the seam stays flat.</summary>
public sealed class ContactDescriptionDto
{
    public required Guid ContactId { get; set; }

    public required string DisplayName { get; set; }

    public string? Nickname { get; set; }

    public string? Pronouns { get; set; }

    public string[] Tags { get; set; } = [];

    public string? Notes { get; set; }

    public string[] Relations { get; set; } = [];
}
