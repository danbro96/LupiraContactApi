namespace LupiraContactApi.Core.Application;

/// <summary>A contact whose state changed since a sync token: its resource UID and current ETag, or a tombstone.</summary>
public sealed record DavChange(string Uid, string? Etag, bool Deleted);
