namespace LupiraContactApi.Core.Dtos.Me;

/// <summary>The resolved local identity of the caller: the stable <see cref="PrincipalId"/> plus current
/// email/display name — the same identity shape (<c>principalId</c>/<c>email</c>/<c>displayName</c>) the
/// platform uses everywhere it returns a person.</summary>
public sealed class MeDto
{
    public required Guid PrincipalId { get; set; }
    public required string Email { get; set; }
    public string? DisplayName { get; set; }

    /// <summary>The caller's own contact ("this card is me"), when linked — the default circles focus.</summary>
    public Guid? ContactId { get; set; }
}
