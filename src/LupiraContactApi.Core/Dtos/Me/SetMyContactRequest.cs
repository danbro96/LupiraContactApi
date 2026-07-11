namespace LupiraContactApi.Dtos.Me;

/// <summary>Links the caller's identity to its own contact ("this card is me") — the default circles focus.</summary>
public sealed class SetMyContactRequest
{
    public required Guid ContactId { get; set; }
}
