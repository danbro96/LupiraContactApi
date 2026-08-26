using System.Security.Cryptography;
using System.Text;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>Stable Guid derived from a natural key (the external uid) — so a delete-then-recreate of the same uid lands on the same event stream and resurrects it rather than creating a duplicate.</summary>
public static class DeterministicGuid
{
    public static Guid From(string value) => new(MD5.HashData(Encoding.UTF8.GetBytes(value)));
}
