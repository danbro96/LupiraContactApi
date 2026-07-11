namespace LupiraContactApi.Dtos.Me;

/// <summary>The resolved local identity of the caller.</summary>
public sealed class MeDto
{
    public required Guid Id { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>The caller's own contact ("this card is me"), when linked — the default circles focus.</summary>
    public Guid? ContactId { get; set; }
}
