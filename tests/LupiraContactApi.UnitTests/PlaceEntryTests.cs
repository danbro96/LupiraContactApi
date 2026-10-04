using JasperFx.Events;
using LupiraContactApi.Core.Domain.PlaceEntries;
using LupiraContactApi.Core.Domain.PlaceEntries.Events;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>Door and gate codes per place: one record per place, codes upserted by id, unchanged restatements append nothing.</summary>
public class PlaceEntryTests
{
    private static readonly Guid Place = Guid.NewGuid();

    private static PlaceEntry Fold(IEnumerable<object> events)
    {
        var entry = new PlaceEntry();
        foreach (var data in events)
        {
            switch (data)
            {
                case EntryCodeSet d: entry.Apply(Ev(d)); break;
                case EntryCodeRemoved d: entry.Apply(Ev(d)); break;
            }
        }

        return entry;
    }

    private static IEvent<T> Ev<T>(T data)
        where T : notnull
    {
        var e = Event.For(data);
        e.StreamId = PlaceEntry.StreamIdOf(Place);
        return e;
    }

    [Fact]
    public void A_place_has_one_record_whatever_the_caller()
    {
        Assert.Equal(PlaceEntry.StreamIdOf(Place), PlaceEntry.StreamIdOf(Place));
        Assert.NotEqual(PlaceEntry.StreamIdOf(Place), PlaceEntry.StreamIdOf(Guid.NewGuid()));
    }

    [Fact]
    public void Codes_upsert_by_id_and_restating_appends_nothing()
    {
        var port = Guid.NewGuid();
        var entry = Fold(PlaceEntry.Set(null, Place, port, "Port", "1234", null));
        Assert.Empty(PlaceEntry.Set(entry, Place, port, "Port", "1234", null));

        entry = Fold([.. PlaceEntry.Set(null, Place, port, "Port", "1234", null), .. PlaceEntry.Set(entry, Place, port, "Port", "9876", "since May")]);
        var code = Assert.Single(entry.Codes);
        Assert.Equal(("9876", "since May"), (code.Code, code.Note));
    }

    [Fact]
    public void Removing_an_unknown_code_appends_nothing()
    {
        var entry = Fold(PlaceEntry.Set(null, Place, Guid.NewGuid(), "Gate", "42", null));
        Assert.Empty(entry.Remove(Guid.NewGuid()));
        Assert.Single(entry.Remove(entry.Codes[0].Id));
    }
}
