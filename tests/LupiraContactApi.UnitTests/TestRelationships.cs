using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;

namespace LupiraContactApi.UnitTests;

internal static class TestRelationships
{
    /// <summary>A live relationship "<paramref name="other"/> is <paramref name="self"/>'s <paramref name="kind"/>".</summary>
    public static Relationship Of(Guid self, Guid other, ContactRelationKind kind, bool ended = false)
    {
        var key = RelationshipKey.Of(self, other, kind);
        return new Relationship { Id = key.StreamId, Low = key.Low, High = key.High, Kind = key.Kind, Ended = ended };
    }
}
