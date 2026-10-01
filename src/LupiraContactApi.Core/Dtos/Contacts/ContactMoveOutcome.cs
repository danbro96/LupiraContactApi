using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Dtos.Contacts;

/// <summary>What a move did to one contact. <c>Unchanged</c> = already in the target book; <c>Skipped</c> = a group member
/// left in place because it lives outside the group's book.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactMoveOutcome>))]
public enum ContactMoveOutcome
{
    Moved,
    Unchanged,
    NotFound,
    Forbidden,
    Skipped,
}
