using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Contacts;

/// <summary>The medium a reach channel uses. Social/IM handles are modeled separately as <see cref="ContactSocialProfile"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReachMedium>))]
public enum ReachMedium { Email, Phone }
