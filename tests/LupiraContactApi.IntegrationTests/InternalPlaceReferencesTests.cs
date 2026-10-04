using System.Net.Http.Json;
using Lupira.Contracts.PlaceRefs;
using Lupira.Testing.Postgres;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.Contacts;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>The place-reference check seam for geo's orphan sweep: live and deleted counts per requested place id,
/// residency history and deceased contacts live, deleted contacts counted apart, unrequested ids excluded.</summary>
public sealed class InternalPlaceReferencesTests(ContactApiTestFactory factory) : IntegrationTest(factory)
{
    private const string Email = "alice@x.test";

    private static async Task<PlaceReferencesResponse> CheckAsync(HttpClient svc, params Guid[] placeIds)
    {
        var resp = await svc.PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [.. placeIds] });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<PlaceReferencesResponse>())!;
    }

    [Fact]
    public async Task Counts_current_and_moved_out_residencies_for_requested_ids_only()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var home = Guid.NewGuid();
        var former = Guid.NewGuid();
        var unrequested = Guid.NewGuid();

        var jane = await CreateContactAsync(api, book, "Jane", "Doe");
        await AddResidencyAsync(api, jane.Id, home);
        await AddResidencyAsync(api, jane.Id, former, movedOut: new FuzzyDate(2020));

        var john = await CreateContactAsync(api, book, "John", "Doe");
        await AddResidencyAsync(api, john.Id, home);
        await AddResidencyAsync(api, john.Id, unrequested, ContactAddressType.Work);

        var result = await CheckAsync(Factory.ScopedClient("svc@x.test", "internal:read"), home, former, Guid.NewGuid());

        var byId = result.Places.ToDictionary(p => p.PlaceId, p => (p.LiveCount, p.DeletedCount));
        Assert.Equal((2, 0), byId[home]);
        Assert.Equal((1, 0), byId[former]);
        Assert.Equal(2, byId.Count);   // zero-ref requested id omitted, unrequested id absent
    }

    [Fact]
    public async Task Counts_deceased_as_live_and_deleted_contacts_apart()
    {
        var api = Factory.ApiClient(Email);
        var book = await CreateAddressBookAsync(api);
        var keptPlace = Guid.NewGuid();
        var lostPlace = Guid.NewGuid();
        var sharedPlace = Guid.NewGuid();

        var deceased = await CreateContactAsync(api, book, "Alan", "Turing");
        await AddResidencyAsync(api, deceased.Id, keptPlace);
        await AddResidencyAsync(api, deceased.Id, sharedPlace, ContactAddressType.Work);
        (await api.PutAsJsonAsync($"/contacts/{deceased.Id}/deceased", new SetDeceasedRequest { DeathDate = null }))
            .EnsureSuccessStatusCode();

        var deleted = await CreateContactAsync(api, book, "Gone", "Soon");
        await AddResidencyAsync(api, deleted.Id, lostPlace);
        await AddResidencyAsync(api, deleted.Id, sharedPlace, ContactAddressType.Work);
        (await api.DeleteAsync($"/contacts/{deleted.Id}")).EnsureSuccessStatusCode();

        var result = await CheckAsync(Factory.ScopedClient("svc@x.test", "internal:read"), keptPlace, lostPlace, sharedPlace);

        var byId = result.Places.ToDictionary(p => p.PlaceId, p => (p.LiveCount, p.DeletedCount));
        Assert.Equal((1, 0), byId[keptPlace]);
        Assert.Equal((0, 1), byId[lostPlace]);
        Assert.Equal((1, 1), byId[sharedPlace]);
        Assert.Equal(3, byId.Count);
    }

    [Fact]
    public async Task Caps_the_id_batch_and_rejects_empty()
    {
        var svc = Factory.ScopedClient("svc@x.test", "internal:read");
        var empty = await svc.PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [] });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, empty.StatusCode);

        var oversize = await svc.PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [.. Enumerable.Range(0, 1001).Select(_ => Guid.NewGuid())] });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, oversize.StatusCode);
    }

    [Fact]
    public async Task Requires_the_internal_scope()
    {
        var anon = await Factory.AnonymousClient().PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [Guid.NewGuid()] });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anon.StatusCode);

        var user = await Factory.ApiClient(Email).PostAsJsonAsync("/internal/contacts/place-references:check",
            new CheckPlaceReferencesRequest { PlaceIds = [Guid.NewGuid()] });
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, user.StatusCode);
    }
}
