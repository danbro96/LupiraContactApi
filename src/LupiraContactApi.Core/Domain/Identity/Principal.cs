using LupiraContactApi.Core.Domain.Contacts;

namespace LupiraContactApi.Core.Domain.Identity;

/// <summary>The shared identity plus <see cref="ContactId"/>, which links the principal to its own
/// <see cref="Contact"/> ("my details" / "what I attended").</summary>
public sealed class Principal : Lupira.Identity.Marten.Principal
{
    public Guid? ContactId { get; set; }
}
