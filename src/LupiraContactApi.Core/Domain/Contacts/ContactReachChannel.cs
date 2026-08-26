namespace LupiraContactApi.Core.Domain.Contacts;

/// <summary>A way to reach a contact — an email address or phone number — with an open <see cref="Type"/> token
/// (well-known: home/work/cell/fax/pager/other) and a per-medium <see cref="Preferred"/> flag. A record so event
/// payloads stay immutable.</summary>
public sealed record ContactReachChannel(ReachMedium Medium, string Value, string? Type, bool Preferred);
