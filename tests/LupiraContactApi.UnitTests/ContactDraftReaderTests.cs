using System.Text;
using Lupira.Results;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Shared;
using Xunit;

namespace LupiraContactApi.UnitTests;

public class ContactDraftReaderTests
{
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid Bo = Guid.NewGuid();

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void A_person_card_becomes_a_create_ready_draft()
    {
        const string file = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u-1\r\nFN:Jane Q Doe\r\nN:Doe;Jane;Q;;\r\nNICKNAME:JD\r\nORG:Acme;Sales\r\n" +
            "TEL;TYPE=cell,pref:+4670\r\nTEL;TYPE=home,pref:+4608\r\nEMAIL:JANE@x.test \r\nEMAIL:jane@X.test\r\nBDAY:19900215\r\nNOTE:Met at work\r\nEND:VCARD\r\n";

        var d = Assert.Single(ContactDraftReader.Read(Anna, Utf8(file), null).Value!);

        Assert.Equal(ContactKind.Individual, d.Kind);
        Assert.Equal(("Jane", "Q", "Doe", "JD", "Acme"), (d.GivenName, d.MiddleName, d.FamilyName, d.Nickname, d.Organization));
        Assert.Equal(
            [new ContactReachChannel(ReachMedium.Phone, "+4670", "cell", true), new ContactReachChannel(ReachMedium.Phone, "+4608", "home", false), new ContactReachChannel(ReachMedium.Email, "JANE@x.test", null, false)],
            d.Channels);
        Assert.Equal(new PartialDate(1990, 2, 15), d.Birthday);
        Assert.Equal("Met at work", d.Notes);
    }

    [Theory]
    [InlineData("BEGIN:VCARD\r\nVERSION:4.0\r\nKIND:org\r\nFN:Trattoria Nonna\r\nORG:Trattoria Nonna\r\nTEL:+4610\r\nEND:VCARD\r\n")]
    [InlineData("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Trattoria Nonna\r\nN:;;;;\r\nORG:Trattoria Nonna;\r\nTEL:+4610\r\nEND:VCARD\r\n")]
    public void An_organization_card_carries_its_name_as_the_given_name(string file)
    {
        var d = Assert.Single(ContactDraftReader.Read(Anna, Utf8(file), null).Value!);

        Assert.Equal(ContactKind.Organization, d.Kind);
        Assert.Equal("Trattoria Nonna", d.GivenName);
        Assert.Null(d.FamilyName);
        Assert.Null(d.Organization);
    }

    [Fact]
    public void The_source_key_is_stable_per_caller_and_card()
    {
        const string withUid = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u-1\r\nN:Doe;Jane;;;\r\nEND:VCARD\r\n";
        const string withoutUid = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Doe;Jane;;;\r\nEND:VCARD\r\nBEGIN:VCARD\r\nVERSION:3.0\r\nN:Doe;John;;;\r\nEND:VCARD\r\n";

        string[] Keys(Guid caller, string file) => [.. ContactDraftReader.Read(caller, Utf8(file), null).Value!.Select(d => d.SourceKey)];

        Assert.Equal(Keys(Anna, withUid), Keys(Anna, withUid.Replace("N:Doe;Jane", "N:Doe-Smith;Jane")));
        Assert.NotEqual(Keys(Anna, withUid), Keys(Bo, withUid));
        Assert.Equal(Keys(Anna, withoutUid), Keys(Anna, withoutUid));
        Assert.Equal(2, Keys(Anna, withoutUid).Distinct().Count());
        Assert.All(Keys(Anna, withoutUid), k => Assert.StartsWith("import-", k));
    }

    [Fact]
    public void A_latin1_file_without_a_declared_charset_is_decoded()
    {
        var file = Encoding.Latin1.GetBytes("BEGIN:VCARD\r\nVERSION:2.1\r\nN:Öberg;Åsa\r\nEND:VCARD\r\n");

        var d = Assert.Single(ContactDraftReader.Read(Anna, file, null).Value!);

        Assert.Equal(("Åsa", "Öberg"), (d.GivenName, d.FamilyName));
    }

    [Fact]
    public void A_file_without_cards_is_invalid()
    {
        var result = ContactDraftReader.Read(Anna, Utf8("not a contacts file"), "utf-8");

        Assert.Equal(OpStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }
}
