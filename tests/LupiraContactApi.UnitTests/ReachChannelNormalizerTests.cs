using LupiraContactApi.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

public class ReachChannelNormalizerTests
{
    static ContactReachChannel Ch(ReachMedium m, string v, string? t = null, bool p = false) => new(m, v, t, p);

    [Fact]
    public void Trims_value_lowercases_type_and_drops_blanks()
    {
        var result = ReachChannelNormalizer.Normalize([Ch(ReachMedium.Email, "  Jane@x.test ", "HOME"), Ch(ReachMedium.Phone, "   ")]);
        var only = Assert.Single(result);
        Assert.Equal("Jane@x.test", only.Value);
        Assert.Equal("home", only.Type);
    }

    [Fact]
    public void Dedupes_by_medium_and_value_case_insensitively_first_wins()
    {
        var result = ReachChannelNormalizer.Normalize([Ch(ReachMedium.Email, "a@x", "home"), Ch(ReachMedium.Email, "A@X", "work")]);
        var only = Assert.Single(result);
        Assert.Equal("home", only.Type);   // first casing/entry wins
    }

    [Fact]
    public void Same_value_different_medium_is_kept()
    {
        var result = ReachChannelNormalizer.Normalize([Ch(ReachMedium.Email, "123"), Ch(ReachMedium.Phone, "123")]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Preferred_conflict_is_detected_per_medium()
    {
        Assert.True(ReachChannelNormalizer.HasPreferredConflict([Ch(ReachMedium.Phone, "1", p: true), Ch(ReachMedium.Phone, "2", p: true)]));
        Assert.False(ReachChannelNormalizer.HasPreferredConflict([Ch(ReachMedium.Phone, "1", p: true), Ch(ReachMedium.Email, "a@x", p: true)]));
    }
}
