using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Relationships;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>The sync adapter's card: the ETag covers the contact and the relationships it shows, and only what the card carries.</summary>
public class DavCardTests
{
    private static readonly Contact Jane = new() { Id = Guid.NewGuid(), ExternalId = "jane@x", GivenName = "Jane", ContentHash = "abc" };
    private static readonly Guid Other = Guid.NewGuid();

    private static string Etag(params ResolvedRelation[] relations) => new DavCard(Jane, relations).Etag;

    private static ResolvedRelation Friend(string? label = null, DateOnly? since = null, string? note = null, bool ended = false) =>
        new(Other, ContactRelationKind.Friend, label, since, note, ended, null);

    [Fact]
    public void Without_relationships_the_etag_is_the_contacts_own_hash() =>
        Assert.Equal("abc", Etag());

    [Fact]
    public void What_the_card_carries_moves_the_etag()
    {
        var bare = Etag(Friend());
        Assert.NotEqual(Etag(), bare);
        Assert.NotEqual(bare, Etag(Friend(label: "pal")));
        Assert.NotEqual(bare, Etag(Friend(since: new DateOnly(2001, 1, 1))));
        Assert.NotEqual(bare, Etag(Friend(ended: true)));
    }

    [Fact]
    public void The_note_is_not_on_the_card_so_it_leaves_the_etag() =>
        Assert.Equal(Etag(Friend()), Etag(Friend(note: "school")));

    [Theory]
    [InlineData("Q", null)]
    [InlineData(null, "Janie")]
    public void A_card_carrying_a_middle_name_or_nickname_has_its_own_etag(string? middle, string? nickname)
    {
        var named = new Contact { Id = Jane.Id, ExternalId = Jane.ExternalId, GivenName = "Jane", MiddleName = middle, Nickname = nickname, ContentHash = "abc" };
        Assert.NotEqual("abc", new DavCard(named, []).Etag);
        Assert.NotEqual(new DavCard(Jane, [Friend()]).Etag, new DavCard(named, [Friend()]).Etag);
    }

    [Fact]
    public void The_card_carries_the_middle_name_and_nickname()
    {
        var vcf = new DavCard(new Contact { ExternalId = "jane@x", GivenName = "Jane", MiddleName = "Q", FamilyName = "Smith", Nickname = "Janie" }, []).Vcard;
        Assert.Contains("N:Smith;Jane;Q;;\r\n", vcf);
        Assert.Contains("NICKNAME:Janie\r\n", vcf);
    }

    [Fact]
    public void The_card_lists_each_relationship_as_related()
    {
        var vcf = new DavCard(Jane, [Friend(label: "pal")]).Vcard;
        Assert.Contains($"RELATED;TYPE=friend;X-LUPIRA-LABEL=pal:urn:uuid:{Other:D}\r\n", vcf);
    }
}
