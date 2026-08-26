using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>Type of a contact's postal address.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContactAddressType>))]
public enum ContactAddressType
{
    Home,
    Work,
    Other,
}
