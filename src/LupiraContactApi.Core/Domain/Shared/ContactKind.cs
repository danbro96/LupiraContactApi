using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>What the contact card represents — vCard <c>KIND</c>. A business/venue (a provider referenced from
/// bookings, say) is an <c>Organization</c>: no birthday, employer, or kinship applies. <c>Individual</c> is ordinal 0
/// so pre-existing events replay as persons.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactKind>))]
public enum ContactKind
{
    Individual,
    Organization,
}
