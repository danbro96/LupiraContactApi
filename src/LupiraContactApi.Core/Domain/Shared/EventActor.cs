using JasperFx.Events;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>
/// Reads the acting principal from Marten event-metadata headers. Mutations stamp the <c>actor</c> header
/// (the caller's principal id); aggregates surface it on attribution fields. Requires
/// <c>opts.Events.MetadataConfig.HeadersEnabled = true</c>.
/// </summary>
public static class EventActor
{
    public const string HeaderKey = "actor";

    /// <summary>The actor header value, or <c>null</c> when none was stamped.</summary>
    public static string? Of(IEvent e) =>
        e.Headers is { } h && h.TryGetValue(HeaderKey, out var v) ? v as string : null;
}
