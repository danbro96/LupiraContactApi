using LupiraContactApi.Application;
using LupiraContactApi.Auth;
using LupiraContactApi.Domain;
using LupiraContactApi.Serialization;
using Marten;

namespace LupiraContactApi.Dav;

/// <summary>
/// The address-book half of the internal /dav-backend contract consumed by the LupiraDavApi gateway.
/// Acts on behalf of the principal named by the path {email} (the gateway verified the human credential
/// via LDAP Basic auth); JIT-provisions the principal and its personal book on first sight, mirroring
/// the old first-PROPFIND self-provision behavior.
/// </summary>
public sealed class DavBackendHandler(
    IQuerySession session,
    AccessResolver access,
    PrincipalDirectory principals,
    AddressBookService books,
    ContactService contacts,
    DavChangeFeed feed)
{
    public async Task<IResult> CollectionsAsync(string email, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        await books.BootstrapPersonalAsync(principal.Id, ct);

        var accessible = (await books.ListAsync(principal.Id, ct)).Value!;
        var token = await feed.CurrentTokenAsync(ct);
        return TypedResults.Ok(new DavCollectionsDto
        {
            Principal = new DavPrincipalDto { DisplayName = principal.DisplayName ?? principal.Email },
            Collections = [.. accessible.Select(b => new DavCollectionDto
            {
                Id = b.Id,
                Kind = DavCollectionKind.AddressBook,
                DisplayName = b.DisplayName ?? b.Slug,
                Ctag = $"seq-{token}",
                SyncToken = token.ToString(),
            })],
        });
    }

    public async Task<IResult> QueryAsync(string email, Guid collectionId, DavQueryRequest body, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        if (!await access.CanReadAddressBookAsync(principal.Id, collectionId, ct)) return TypedResults.NotFound();

        var live = await session.Query<Contact>()
            .Where(c => c.AddressBookId == collectionId && c.DeletedAt == null).ToListAsync(ct);
        IEnumerable<Contact> selected = live;
        if (body.Uids is { Count: > 0 } uids)
        {
            var set = uids.ToHashSet(StringComparer.Ordinal);
            selected = live.Where(c => set.Contains(c.ExternalId));
        }
        // Start/End: time-range does not apply to address books — ignored by design.

        return TypedResults.Ok(new DavResourcesDto
        {
            Resources = [.. selected.Select(c => new DavResourceDto
            {
                Uid = c.ExternalId,
                Etag = c.ContentHash,
                Content = body.IncludeContent ? VCardSerializer.From(c) : null,
            })],
        });
    }

    public async Task<IResult> GetResourceAsync(string email, Guid collectionId, string uid, HttpContext ctx, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        if (!await access.CanReadAddressBookAsync(principal.Id, collectionId, ct)) return TypedResults.NotFound();

        var c = await session.LoadAsync<Contact>(DeterministicGuid.From(uid), ct);
        if (c is null || c.DeletedAt is not null || c.AddressBookId != collectionId) return TypedResults.NotFound();

        ctx.Response.Headers.ETag = $"\"{c.ContentHash}\"";
        return TypedResults.Text(VCardSerializer.From(c), "text/vcard; charset=utf-8");
    }

    public async Task<IResult> PutResourceAsync(string email, Guid collectionId, string uid, HttpContext ctx, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        using var reader = new StreamReader(ctx.Request.Body);
        var raw = await reader.ReadToEndAsync(ct);
        var (ifMatch, ifNoneMatchStar) = ParsePreconditions(ctx.Request.Headers.IfMatch, ctx.Request.Headers.IfNoneMatch);

        var result = await contacts.PutVcfAsync(principal.Id, collectionId, uid, raw, ifMatch, ifNoneMatchStar, ct);
        if (result.Status == OpStatus.Ok && result.Value is { } w)
        {
            ctx.Response.Headers.ETag = $"\"{w.Etag}\"";
            return TypedResults.StatusCode(w.Created ? StatusCodes.Status201Created : StatusCodes.Status204NoContent);
        }
        return TypedResults.StatusCode(DavStatus(result.Status));
    }

    public async Task<IResult> DeleteResourceAsync(string email, Guid collectionId, string uid, HttpContext ctx, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        var (ifMatch, _) = ParsePreconditions(ctx.Request.Headers.IfMatch, ctx.Request.Headers.IfNoneMatch);
        var result = await contacts.DeleteByUidAsync(principal.Id, collectionId, uid, ifMatch, ct);
        return TypedResults.StatusCode(DavStatus(result.Status));
    }

    public async Task<IResult> ChangesAsync(string email, Guid collectionId, string? since, CancellationToken ct)
    {
        var principal = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        if (!await access.CanReadAddressBookAsync(principal.Id, collectionId, ct)) return TypedResults.NotFound();

        // An unparsable/absent token degrades to the full live listing — self-healing resync.
        long? parsed = long.TryParse(since, out var t) ? t : null;
        var (token, changes) = await feed.ChangesSinceAsync(collectionId, parsed, ct);
        return TypedResults.Ok(new DavChangesDto
        {
            SyncToken = token.ToString(),
            Changed = [.. changes.Where(c => !c.Deleted).Select(c => new DavChangeDto { Uid = c.Uid, Etag = c.Etag! })],
            Deleted = [.. changes.Where(c => c.Deleted).Select(c => c.Uid)],
        });
    }

    /// <summary>An <c>If-Match</c> of <c>*</c> (or empty) is "no specific tag"; quotes are stripped from a
    /// concrete tag. <c>If-None-Match: *</c> is the "create only if absent" guard.</summary>
    internal static (string? IfMatch, bool IfNoneMatchStar) ParsePreconditions(string? ifMatchHeader, string? ifNoneMatchHeader)
    {
        string? ifMatch = null;
        var im = ifMatchHeader?.Trim();
        if (!string.IsNullOrEmpty(im) && im != "*") ifMatch = im.Trim('"');
        var inm = ifNoneMatchHeader?.Trim() ?? "";
        return (ifMatch, inm == "*");
    }

    internal static int DavStatus(OpStatus status) => status switch
    {
        OpStatus.Ok => StatusCodes.Status204NoContent,
        OpStatus.Forbidden => StatusCodes.Status403Forbidden,
        OpStatus.NotFound => StatusCodes.Status404NotFound,
        OpStatus.Conflict => StatusCodes.Status412PreconditionFailed,
        OpStatus.Invalid => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
}
