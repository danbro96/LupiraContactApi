using JasperFx.Events;
using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The per-section LWW rules on the <see cref="Contact"/> aggregate: stale replays lose, sections stay
/// independent, arrival order doesn't matter, delete absorbs. The wins predicate itself is identical to
/// LupiraCalApi's — the shared parity vectors live in the mobile monorepo.</summary>
public class SectionLwwTests
{
    static long _sequence;
    static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    static IEvent<T> Ev<T>(T data, DateTimeOffset? at = null) where T : class
    {
        var seq = Interlocked.Increment(ref _sequence);
        var e = Event.For(data);
        e.Sequence = seq;
        e.Timestamp = at ?? T0.AddSeconds(seq);
        return e;
    }

    static ContactFields Fields(string given, string? note = null) =>
        new(given, null, "Doe", null, [], null, null, note, null, DisplayNameFormat.Full, ContactKind.Individual);

    static Contact Created(Guid id)
    {
        var c = new Contact();
        c.Apply(Ev(new ContactCreated(id, Guid.NewGuid(), $"{id:N}@x", Fields("Original"))));
        return c;
    }

    [Fact]
    public void Core_revisions_converge_regardless_of_arrival_order()
    {
        var id = Guid.NewGuid();
        var t = T0.AddHours(1);
        var older = new ContactRevised(id, Fields("Older"), t, Guid.NewGuid());
        var newer = new ContactRevised(id, Fields("Newer"), t.AddMinutes(5), Guid.NewGuid());

        var inOrder = Created(id);
        inOrder.Apply(Ev(older));
        inOrder.Apply(Ev(newer));

        var outOfOrder = Created(id);
        outOfOrder.Apply(Ev(newer));
        outOfOrder.Apply(Ev(older));   // stale replay arrives late — must lose

        Assert.Equal("Newer", inOrder.GivenName);
        Assert.Equal("Newer", outOfOrder.GivenName);
        Assert.Equal(inOrder.CoreCmd, outOfOrder.CoreCmd);
    }

    [Fact]
    public void Sections_are_independent()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        var baseTs = c.UpdatedAt;
        var place = Guid.NewGuid();

        c.Apply(Ev(new ContactAddressesReplaced(id, [new ContactPostalAddress { PlaceId = place, Type = ContactAddressType.Home }], baseTs.AddHours(2), Guid.NewGuid())));
        // A newer core edit must not let a stale addresses replace through.
        c.Apply(Ev(new ContactRevised(id, Fields("Newer core"), baseTs.AddHours(3), Guid.NewGuid())));
        c.Apply(Ev(new ContactAddressesReplaced(id, [], baseTs.AddHours(1), Guid.NewGuid())));

        Assert.Equal("Newer core", c.GivenName);
        Assert.Equal(place, Assert.Single(c.Addresses).PlaceId);
    }

    [Fact]
    public void Stale_avatar_and_metadata_replays_lose()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        var baseTs = c.UpdatedAt;

        c.Apply(Ev(new ContactAvatarSet(id, "media:new", baseTs.AddHours(2), Guid.NewGuid())));
        c.Apply(Ev(new ContactAvatarSet(id, "media:stale", baseTs.AddHours(1), Guid.NewGuid())));
        c.Apply(Ev(new ContactMetadataAttached(id, """{"k":"new"}""", baseTs.AddHours(2), Guid.NewGuid())));
        c.Apply(Ev(new ContactMetadataAttached(id, """{"k":"stale"}""", baseTs.AddHours(1), Guid.NewGuid())));

        Assert.Equal("media:new", c.AvatarRef);
        Assert.Equal("""{"k":"new"}""", c.Metadata);
    }

    [Fact]
    public void Mark_and_clear_deceased_share_one_guard()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        var baseTs = c.UpdatedAt;

        c.Apply(Ev(new ContactDeceasedCleared(id, baseTs.AddHours(2), Guid.NewGuid())));
        c.Apply(Ev(new ContactMarkedDeceased(id, new DateOnly(2026, 1, 1), baseTs.AddHours(1), Guid.NewGuid())));   // stale — loses to the newer clear

        Assert.False(c.Deceased);
    }

    [Fact]
    public void Delete_absorbs_later_section_writes()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        c.Apply(Ev(new ContactDeleted(id)));
        c.Apply(Ev(new ContactRevised(id, Fields("After delete"), DateTimeOffset.UtcNow, Guid.NewGuid())));

        Assert.NotNull(c.DeletedAt);
        Assert.Equal("Original", c.GivenName);
    }

    [Fact]
    public void Unstamped_events_apply_in_append_order_and_watermark_tracks()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        c.Apply(Ev(new ContactRevised(id, Fields("First"))));
        var last = Ev(new ContactRevised(id, Fields("Second")));
        c.Apply(last);

        Assert.Equal("Second", c.GivenName);
        Assert.Equal(last.Sequence, c.UpdatedSequence);
    }

    [Fact]
    public void Keyed_create_over_a_deleted_stream_resurrects()
    {
        var id = Guid.NewGuid();
        var c = Created(id);
        c.Apply(Ev(new ContactDeleted(id)));
        c.Apply(Ev(new ContactCreated(id, c.AddressBookId, c.ExternalId, Fields("Reborn"))));

        Assert.Null(c.DeletedAt);
        Assert.Equal("Reborn", c.GivenName);
    }
}
