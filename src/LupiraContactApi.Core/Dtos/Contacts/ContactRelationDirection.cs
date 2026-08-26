using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Dtos.Contacts;

[JsonConverter(typeof(JsonStringEnumConverter<ContactRelationDirection>))]
public enum ContactRelationDirection
{
    Outgoing,
    Incoming,
}
