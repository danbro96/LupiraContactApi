using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LupiraContactApi.Core.Application;

/// <summary>Feed cursor: <c>UpdatedSequence</c> watermark + a token of the caller's readable address books. Grants
/// and revokes move no contact's sequence, so a scope mismatch restarts the stream.</summary>
public readonly record struct SyncCursor(long Sequence, string Scope)
{
    public static string ScopeOf(IEnumerable<Guid> readableAddressBookIds)
    {
        var joined = string.Join(',', readableAddressBookIds.Order().Select(id => id.ToString("N")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
    }

    /// <summary>A pre-scope cursor (bare sequence) parses with an empty scope, so it restarts once.</summary>
    public static bool TryParse(string value, out SyncCursor cursor)
    {
        cursor = default;
        var dot = value.IndexOf('.');
        if (!long.TryParse(dot < 0 ? value : value[..dot], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)) return false;
        cursor = new SyncCursor(sequence, dot < 0 ? string.Empty : value[(dot + 1)..]);
        return true;
    }

    /// <summary>Where an unpaged feed resumes for <paramref name="scope"/>: 0 = a full sync (no cursor, a cursor from another
    /// scope, or one at the start). False when <paramref name="since"/> is not a cursor at all.</summary>
    public static bool TryResume(string? since, string scope, out long sequence)
    {
        sequence = 0;
        if (string.IsNullOrWhiteSpace(since)) return true;
        if (!TryParse(since, out var given)) return false;
        if (given.Scope == scope) sequence = given.Sequence;
        return true;
    }

    public override string ToString() => $"{Sequence}.{Scope}";
}
