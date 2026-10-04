using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>How a contact uses a place: where they live, a vacation home, where they work, or something else.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactAddressType>))]
public enum ContactAddressType
{
    Home,
    Vacation,
    Work,
    Other,
}
