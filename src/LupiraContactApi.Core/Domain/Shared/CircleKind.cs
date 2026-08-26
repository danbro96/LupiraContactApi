using System.Text.Json.Serialization;

namespace LupiraContactApi.Core.Domain.Shared;

/// <summary>An inferred social cohort around a focus contact (computed on read, never stored).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CircleKind>))]
public enum CircleKind { CloseFamily, ExtendedFamily, Friends, Colleagues, Household }
