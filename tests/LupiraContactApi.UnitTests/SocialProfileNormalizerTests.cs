using LupiraContactApi.Core.Domain;
using Xunit;

namespace LupiraContactApi.UnitTests;

public class SocialProfileNormalizerTests
{
    static ContactSocialProfile Normalize(string service, string handle, string? url = null, bool preferred = false) =>
        SocialProfileNormalizer.Normalize(new ContactSocialProfile { Service = service, Handle = handle, Url = url, Preferred = preferred });

    [Fact]
    public void Service_is_lowercased_and_handle_trimmed_of_whitespace_and_at()
    {
        var p = Normalize(" Telegram ", " @Jane ");
        Assert.Equal("telegram", p.Service);
        Assert.Equal("Jane", p.Handle);
    }

    [Theory]
    [InlineData("telegram", "jane", "https://t.me/jane")]
    [InlineData("messenger", "jane", "https://m.me/jane")]
    [InlineData("facebook", "jane", "https://m.me/jane")]
    [InlineData("whatsapp", "46701234567", "https://wa.me/46701234567")]
    [InlineData("signal", "jane.01", "https://signal.me/#p/jane.01")]
    [InlineData("instagram", "jane", "https://instagram.com/jane")]
    [InlineData("linkedin", "jane", "https://www.linkedin.com/in/jane")]
    [InlineData("x", "jane", "https://x.com/jane")]
    [InlineData("twitter", "jane", "https://x.com/jane")]
    [InlineData("github", "jane", "https://github.com/jane")]
    public void Url_is_derived_for_well_known_services(string service, string handle, string expected) =>
        Assert.Equal(expected, Normalize(service, handle).Url);

    [Fact]
    public void Matrix_keeps_the_at_sign_the_id_needs_it()
    {
        var p = Normalize("matrix", "@jane:example.org");
        Assert.Equal("@jane:example.org", p.Handle);
        Assert.Equal("https://matrix.to/#/@jane:example.org", p.Url);
    }

    [Theory]
    [InlineData("discord")]
    [InlineData("mastodon")]
    [InlineData("carrier-pigeon")]
    public void Unknown_or_underivable_services_get_no_url(string service) =>
        Assert.Null(Normalize(service, "jane").Url);

    [Fact]
    public void An_explicit_url_is_never_overwritten()
    {
        var p = Normalize("telegram", "jane", "https://t.me/JaneOfficial");
        Assert.Equal("https://t.me/JaneOfficial", p.Url);
    }

    [Fact]
    public void Preferred_passes_through() =>
        Assert.True(Normalize("telegram", "jane", preferred: true).Preferred);
}
