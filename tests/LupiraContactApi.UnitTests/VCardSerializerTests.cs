using LupiraContactApi.Domain;
using LupiraContactApi.Serialization;
using Xunit;

namespace LupiraContactApi.UnitTests;

/// <summary>vCard 3.0 build + line-based parse: round-trip fidelity, escape/unescape ordering, FN fallback,
/// the two BDAY formats, multi-valued EMAIL/TEL, ORG segmentation, and folded-line skipping.</summary>
public class VCardSerializerTests
{
    [Fact]
    public void Build_then_parse_preserves_the_core_fields()
    {
        var vcf = VCardSerializer.Build("uid@x", "Jane Smith", "Jane", "Smith", "Acme",
            ["jane@x.com", "j@y.com"], ["+4612345"], new DateOnly(1990, 2, 15));

        var p = VCardSerializer.ParseVCard(vcf);

        Assert.Equal("Jane Smith", p.FullName);
        Assert.Equal("Jane", p.GivenName);
        Assert.Equal("Smith", p.FamilyName);
        Assert.Equal("Acme", p.Organization);
        Assert.Equal(["jane@x.com", "j@y.com"], p.Emails!);
        Assert.Equal(["+4612345"], p.Phones!);
        Assert.Equal(new DateOnly(1990, 2, 15), p.Birthday);
    }

    [Theory]
    [InlineData("a\\b;c")]      // backslash + semicolon — pins the unescape ordering (\\ unescaped last)
    [InlineData("Doe, John")]   // comma
    [InlineData("Line1\nLine2")] // newline (escaped to \n on the wire, restored on parse)
    [InlineData("Plain Text")]
    public void Special_characters_survive_a_round_trip_in_the_full_name(string value)
    {
        var vcf = VCardSerializer.Build("uid@x", value, null, null, null, null, null, null);
        Assert.Equal(value, VCardSerializer.ParseVCard(vcf).FullName);
    }

    [Fact]
    public void N_property_maps_family_then_given()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nN:Smith;Jane;;;\r\nEND:VCARD\r\n");
        Assert.Equal("Smith", p.FamilyName);
        Assert.Equal("Jane", p.GivenName);
    }

    [Fact]
    public void Missing_FN_is_composed_from_the_name_parts()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nN:Smith;Jane;;;\r\nEND:VCARD\r\n");
        Assert.Equal("Jane Smith", p.FullName);
    }

    [Theory]
    [InlineData("19900215")]      // vCard 3.0 basic date
    [InlineData("1990-02-15")]    // ISO extended date
    public void Birthday_parses_both_formats(string bday)
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nBDAY:{bday}\r\nEND:VCARD\r\n");
        Assert.Equal(new DateOnly(1990, 2, 15), p.Birthday);
    }

    [Fact]
    public void No_emails_or_phones_parse_as_null_lists()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nEND:VCARD\r\n");
        Assert.Null(p.Emails);
        Assert.Null(p.Phones);
    }

    [Fact]
    public void Org_keeps_only_the_first_segment()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nORG:Acme;Sales\r\nEND:VCARD\r\n");
        Assert.Equal("Acme", p.Organization);
    }

    [Fact]
    public void Folded_continuation_lines_are_skipped()
    {
        // The line starting with a space is an RFC 6350 fold; it must not derail parsing.
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:John Doe\r\n  folded-noise\r\nEND:VCARD\r\n");
        Assert.Equal("John Doe", p.FullName);
    }

    [Fact]
    public void Build_emits_related_with_type_label_and_urn_uuid()
    {
        var dad = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
            [new ContactRelation { ToContactId = dad, Kind = ContactRelationKind.Parent, Label = "dad" }]);

        Assert.Contains($"RELATED;TYPE=parent;X-LUPIRA-LABEL=dad:urn:uuid:{dad:D}\r\n", vcf);
    }

    [Fact]
    public void Related_round_trips_kind_target_and_label()
    {
        var dad = Guid.NewGuid();
        var friend = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
        [
            new ContactRelation { ToContactId = dad, Kind = ContactRelationKind.Parent, Label = "dad" },
            new ContactRelation { ToContactId = friend, Kind = ContactRelationKind.Friend },
        ]);

        var p = VCardSerializer.ParseVCard(vcf);

        Assert.Equal(2, p.Relations!.Length);
        Assert.Equal((dad, ContactRelationKind.Parent, "dad"), (p.Relations[0].ToContactId, p.Relations[0].Kind, p.Relations[0].Label));
        Assert.Equal((friend, ContactRelationKind.Friend, null), (p.Relations[1].ToContactId, p.Relations[1].Kind, p.Relations[1].Label));
    }

    [Theory]
    [InlineData("RELATED;TYPE=parent:https://example.com/x")]      // URL target — not ours
    [InlineData("RELATED;TYPE=parent:urn:uuid:not-a-guid")]
    [InlineData("RELATED;TYPE=parent:free text")]
    public void Related_with_non_urn_uuid_value_is_skipped(string line)
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\n{line}\r\nEND:VCARD\r\n");
        Assert.Null(p.Relations);
    }

    [Theory]
    [InlineData("co-worker", ContactRelationKind.Colleague)]
    [InlineData("sweetheart", ContactRelationKind.Partner)]
    [InlineData("kin", ContactRelationKind.Other)]
    [InlineData("muse", ContactRelationKind.Other)]
    [InlineData("CHILD", ContactRelationKind.Child)]   // case-insensitive enum name
    public void Related_type_synonyms_and_unknowns_map(string type, ContactRelationKind expected)
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nRELATED;TYPE={type}:urn:uuid:{Guid.NewGuid():D}\r\nEND:VCARD\r\n");
        Assert.Equal(expected, Assert.Single(p.Relations!).Kind);
    }

    [Fact]
    public void Related_without_type_defaults_to_other()
    {
        var p = VCardSerializer.ParseVCard($"BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nRELATED:urn:uuid:{Guid.NewGuid():D}\r\nEND:VCARD\r\n");
        Assert.Equal(ContactRelationKind.Other, Assert.Single(p.Relations!).Kind);
    }

    [Fact]
    public void Unsafe_label_is_dropped_from_the_param_but_the_line_still_emits()
    {
        var target = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
            [new ContactRelation { ToContactId = target, Kind = ContactRelationKind.Friend, Label = "a;b:c" }]);

        Assert.Contains($"RELATED;TYPE=friend:urn:uuid:{target:D}\r\n", vcf);
        Assert.DoesNotContain("X-LUPIRA-LABEL", vcf);
    }

    [Fact]
    public void Build_without_extras_emits_none_of_the_extension_props()
    {
        var vcf = VCardSerializer.Build("uid@x", "Jane Smith", "Jane", "Smith", null, ["jane@x.com"], null, new DateOnly(1990, 2, 15), []);
        Assert.DoesNotContain("RELATED", vcf);
        Assert.DoesNotContain("X-DEATHDATE", vcf);
        Assert.DoesNotContain("X-LUPIRA-DECEASED", vcf);
        Assert.DoesNotContain("X-SOCIALPROFILE", vcf);
    }

    [Fact]
    public void Deceased_with_date_emits_deathdate_and_round_trips()
    {
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null, deceased: true, deathDate: new DateOnly(2020, 3, 14));
        Assert.Contains("X-DEATHDATE:20200314\r\n", vcf);
        Assert.DoesNotContain("X-LUPIRA-DECEASED", vcf);

        var p = VCardSerializer.ParseVCard(vcf);
        Assert.True(p.Deceased);
        Assert.Equal(new DateOnly(2020, 3, 14), p.DeathDate);
    }

    [Fact]
    public void Deceased_without_date_emits_the_flag_prop_and_round_trips()
    {
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null, deceased: true);
        Assert.Contains("X-LUPIRA-DECEASED:1\r\n", vcf);
        Assert.DoesNotContain("X-DEATHDATE", vcf);

        var p = VCardSerializer.ParseVCard(vcf);
        Assert.True(p.Deceased);
        Assert.Null(p.DeathDate);
    }

    [Fact]
    public void Unparsable_deathdate_still_means_deceased()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nX-DEATHDATE:unknown\r\nEND:VCARD\r\n");
        Assert.True(p.Deceased);
        Assert.Null(p.DeathDate);
    }

    [Fact]
    public void Absent_extension_props_parse_as_null_the_preserve_signal()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nEND:VCARD\r\n");
        Assert.Null(p.Deceased);
        Assert.Null(p.Profiles);
        Assert.Null(p.EmergencyContactIds);
    }

    [Fact]
    public void Social_profiles_round_trip_with_service_pref_and_url()
    {
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null, profiles:
        [
            new ContactSocialProfile { Service = "telegram", Handle = "jane", Url = "https://t.me/jane", Preferred = true },
            new ContactSocialProfile { Service = "discord", Handle = "jane#123" },
        ]);
        Assert.Contains("X-SOCIALPROFILE;TYPE=telegram;X-LUPIRA-PREF=1:https://t.me/jane\r\n", vcf);
        Assert.Contains("X-SOCIALPROFILE;TYPE=discord:jane#123\r\n", vcf);

        var p = VCardSerializer.ParseVCard(vcf);
        Assert.Equal(2, p.Profiles!.Length);
        Assert.Equal(("telegram", "jane", "https://t.me/jane", true), (p.Profiles[0].Service, p.Profiles[0].Handle, p.Profiles[0].Url, p.Profiles[0].Preferred));
        Assert.Equal(("discord", "jane#123", null, false), (p.Profiles[1].Service, p.Profiles[1].Handle, p.Profiles[1].Url, p.Profiles[1].Preferred));
    }

    [Fact]
    public void Social_profile_url_value_derives_the_handle_from_the_last_segment()
    {
        var p = VCardSerializer.ParseVCard("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:x\r\nX-SOCIALPROFILE;TYPE=telegram:https://t.me/jane/\r\nEND:VCARD\r\n");
        var sp = Assert.Single(p.Profiles!);
        Assert.Equal("jane", sp.Handle);
        Assert.Equal("https://t.me/jane/", sp.Url);
    }

    [Fact]
    public void Emergency_related_lines_round_trip_in_order_and_stay_out_of_relations()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var friend = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
            [new ContactRelation { ToContactId = friend, Kind = ContactRelationKind.Friend }],
            emergencyContacts: [first, second]);
        Assert.Contains($"RELATED;TYPE=emergency:urn:uuid:{first:D}\r\n", vcf);

        var p = VCardSerializer.ParseVCard(vcf);
        Assert.Equal([first, second], p.EmergencyContactIds!);
        Assert.Equal(friend, Assert.Single(p.Relations!).ToContactId);
    }

    [Fact]
    public void Ended_relation_round_trips_until_and_ended_flags()
    {
        var ex = Guid.NewGuid();
        var old = Guid.NewGuid();
        var vcf = VCardSerializer.Build("uid@x", "x", null, null, null, null, null, null,
        [
            new ContactRelation { ToContactId = ex, Kind = ContactRelationKind.Spouse, Ended = true, Until = new DateOnly(2024, 6, 1) },
            new ContactRelation { ToContactId = old, Kind = ContactRelationKind.Friend, Ended = true },
        ]);
        Assert.Contains($"RELATED;TYPE=spouse;X-LUPIRA-UNTIL=20240601:urn:uuid:{ex:D}\r\n", vcf);
        Assert.Contains($"RELATED;TYPE=friend;X-LUPIRA-ENDED=1:urn:uuid:{old:D}\r\n", vcf);

        var p = VCardSerializer.ParseVCard(vcf);
        var exEdge = p.Relations!.Single(r => r.ToContactId == ex);
        Assert.True(exEdge.Ended);
        Assert.Equal(new DateOnly(2024, 6, 1), exEdge.Until);
        var oldEdge = p.Relations!.Single(r => r.ToContactId == old);
        Assert.True(oldEdge.Ended);
        Assert.Null(oldEdge.Until);
    }

    [Fact]
    public void Build_is_deterministic_for_identical_input()
    {
        string Make() => VCardSerializer.Build("uid@x", "x", "a", "b", null, ["e@x"], ["1"], new DateOnly(1990, 1, 1),
            [new ContactRelation { ToContactId = Guid.Empty, Kind = ContactRelationKind.Friend }],
            [Guid.Empty], [new ContactSocialProfile { Service = "telegram", Handle = "h" }], true, new DateOnly(2020, 1, 1));
        Assert.Equal(Make(), Make());
    }
}
