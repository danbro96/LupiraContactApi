using LupiraContactApi.Core.Domain.Contacts;

namespace LupiraContactApi.Core.Domain.Identity;

/// <summary>
/// An identity (plain document, JIT-provisioned from Authentik). <see cref="AuthentikSub"/> is the durable anchor;
/// <see cref="Email"/> is the mutable sync/OIDC join key. <see cref="ContactId"/> links the principal to its own
/// <see cref="Contact"/> ("my details" / "what I attended").
/// </summary>
public sealed class Principal
{
    public Guid Id { get; set; }

    public string AuthentikSub { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public Guid? ContactId { get; set; }

    /// <summary>First provisioned. Pre-existing rows carry a reconstructed estimate.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
