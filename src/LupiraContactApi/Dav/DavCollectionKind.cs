using System.Text.Json.Serialization;

namespace LupiraContactApi.Dav;

[JsonConverter(typeof(JsonStringEnumConverter<DavCollectionKind>))]
public enum DavCollectionKind
{
    EventCalendar,
    TodoList,
    AddressBook,
}
