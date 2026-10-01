using LupiraContactApi.Core.Domain.Identity;
using Xunit;

namespace LupiraContactApi.UnitTests;

public class SelfContactNameTests
{
    [Theory]
    [InlineData("Daniel Broström", "Daniel", "Broström")]
    [InlineData("  Anna   Maria  Svensson ", "Anna", "Maria Svensson")]
    [InlineData("Cher", "Cher", null)]
    public void First_token_is_given_and_the_rest_family(string displayName, string given, string? family) =>
        Assert.Equal((given, family), SelfContactName.From(displayName, "x@y.test"));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("Anna.S@Y.test")]
    public void Without_a_real_name_the_email_local_part_is_given(string? displayName) =>
        Assert.Equal(("anna.s", (string?) null), SelfContactName.From(displayName, "anna.s@y.test"));

    [Fact]
    public void Nothing_to_name_it_by_yields_no_name() =>
        Assert.Equal(((string?) null, (string?) null), SelfContactName.From(null, ""));
}
