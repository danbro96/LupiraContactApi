using Marten;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.AddressBooks;
using LupiraContactApi.Dtos.Contacts;
using LupiraContactApi.Dtos.Me;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace LupiraContactApi.IntegrationTests;

/// <summary>Base for integration tests: shares the container fixture, resets Marten data before each test, and
/// provides fixture + vCard helpers. Lives in the "integration" collection so tests run serially against the shared DB.</summary>
[Collection("integration")]
public abstract class IntegrationTest(ContactApiTestFactory factory) : IAsyncLifetime
{
    protected readonly ContactApiTestFactory Factory = factory;

    protected IDocumentStore Store => Factory.Store;

    public async Task InitializeAsync() => await Factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---- REST fixture helpers ----

    protected static async Task<Guid> GetMyIdAsync(HttpClient api)
    {
        var me = await api.GetFromJsonAsync<MeDto>("/me");
        return me!.PrincipalId;
    }

    protected static async Task<Guid> CreateAddressBookAsync(HttpClient api, string slug = "people", string? displayName = "People")
    {
        var resp = await api.PostAsJsonAsync("/address-books", new CreateAddressBookRequest { Slug = slug, DisplayName = displayName });
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<AddressBookDto>();
        return dto!.Id;
    }

    protected static async Task<ContactDto> CreateContactAsync(HttpClient api, Guid addressBookId, string given = "Jane", string family = "Doe", string? email = null)
    {
        var req = new CreateContactRequest { AddressBookId = addressBookId, GivenName = given, FamilyName = family, Channels = email is null ? null : [new ContactReachChannel(ReachMedium.Email, email, null, false)] };
        var resp = await api.PostAsJsonAsync("/contacts", req);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<ContactDto>())!;
    }

    // ---- payload builders ----

    protected static string MinimalVcf(string uid, string fullName, string? email = null)
    {
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
        sb.Append($"UID:{uid}\r\nFN:{fullName}\r\nN:{fullName};;;;\r\n");
        if (email is not null) sb.Append($"EMAIL:{email}\r\n");
        sb.Append("END:VCARD\r\n");
        return sb.ToString();
    }

    /// <summary>PUT a raw vCard blob at the /dav-backend seam with optional ETag preconditions.</summary>
    protected static async Task<HttpResponseMessage> PutVcfAsync(
        HttpClient client, string email, Guid bookId, string uid, string vcf, string? ifMatch = null, bool ifNoneMatchStar = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put,
            $"/dav-backend/u/{Uri.EscapeDataString(email)}/collections/{bookId}/resources/{uid}");
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        if (ifNoneMatchStar) req.Headers.TryAddWithoutValidation("If-None-Match", "*");
        req.Content = new StringContent(vcf, Encoding.UTF8, "text/vcard");
        return await client.SendAsync(req);
    }
}
