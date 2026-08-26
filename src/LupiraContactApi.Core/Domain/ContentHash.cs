using System.Security.Cryptography;
using System.Text;

namespace LupiraContactApi.Core.Domain;

/// <summary>Strong content validator: hash of the canonical content (see <see cref="ContactContent"/>). Sync surfaces consume it as an opaque version tag.</summary>
public static class ContentHash
{
    public static string Of(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}
