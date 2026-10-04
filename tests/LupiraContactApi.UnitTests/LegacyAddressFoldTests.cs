using JasperFx.Events;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Contacts.Events;
using LupiraContactApi.Core.Domain.Residencies.Events;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Upgrades;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The residencies migration: legacy address lists replayed exactly as the old snapshot applied them — last writer
/// wins per (occurredAt, commandId), a deleted contact ignores address writes — then one residency per surviving entry.</summary>
public class LegacyAddressFoldTests
{
    private static readonly Guid Contact = Guid.NewGuid();
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static long _seq;

    private static IEvent Ev<T>(T data, DateTimeOffset? at = null)
        where T : notnull
    {
        var e = Event.For(data);
        e.StreamId = Contact;
        e.Sequence = Interlocked.Increment(ref _seq);
        e.Timestamp = at ?? T0;
        return e;
    }

    private static ContactPostalAddress Home(Guid place, FuzzyDate? movedOut = null) => new() { PlaceId = place, Type = ContactAddressType.Home, MovedOut = movedOut };

    [Fact]
    public void The_latest_writer_wins_even_out_of_order()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var addresses = LegacyAddressFold.Addresses(
        [
            Ev(new ContactAddressesReplaced(Contact, [Home(a)], T0.AddHours(2), Guid.NewGuid())),
            Ev(new ContactAddressesReplaced(Contact, [Home(b)], T0.AddHours(1), Guid.NewGuid())),   // stale offline write
        ]);

        Assert.Equal(a, Assert.Single(addresses[Contact]).PlaceId);
    }

    [Fact]
    public void A_deleted_contact_ignores_address_writes_until_restored()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        IEvent[] events =
        [
            Ev(new ContactAddressesReplaced(Contact, [Home(a)])),
            Ev(new ContactDeleted(Contact)),
            Ev(new ContactAddressesReplaced(Contact, [Home(b)]), T0.AddHours(1)),
        ];
        Assert.Equal(a, Assert.Single(LegacyAddressFold.Addresses(events)[Contact]).PlaceId);

        var restored = LegacyAddressFold.Addresses([.. events, Ev(new ContactRestored(Contact)), Ev(new ContactAddressesReplaced(Contact, [Home(c)]), T0.AddHours(2))]);
        Assert.Equal(c, Assert.Single(restored[Contact]).PlaceId);
    }

    [Fact]
    public void Each_entry_starts_a_residency_with_a_stable_id()
    {
        var addresses = LegacyAddressFold.Addresses([Ev(new ContactAddressesReplaced(Contact, [Home(Guid.NewGuid()), Home(Guid.NewGuid(), new FuzzyDate(2015))]))]);

        var first = LegacyAddressFold.Residencies(addresses);
        var again = LegacyAddressFold.Residencies(addresses);
        Assert.Equal(first.Select(x => x.StreamId), again.Select(x => x.StreamId));
        var started = first.Select(x => Assert.IsType<ResidencyStarted>(Assert.Single(x.Events))).ToList();
        Assert.Equal([null, new FuzzyDate(2015)], started.Select(s => s.MovedOut));
        Assert.All(started, s => Assert.Equal(Contact, s.ContactId));
    }

    [Fact]
    public void An_emptied_list_migrates_nothing() =>
        Assert.Empty(LegacyAddressFold.Addresses([Ev(new ContactAddressesReplaced(Contact, [Home(Guid.NewGuid())])), Ev(new ContactAddressesReplaced(Contact, []), T0.AddHours(1))]));
}
