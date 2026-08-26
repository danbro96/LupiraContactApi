using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>How a contact's DisplayName renders. Rendering-only — excluded from the content hash. <c>Full</c> is ordinal 0 so old events replay to today's behavior.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DisplayNameFormat>))]
public enum DisplayNameFormat { Full, FirstLast, NickName }
