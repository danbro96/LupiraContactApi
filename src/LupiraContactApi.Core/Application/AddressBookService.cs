using Lupira.Primitives;
using Lupira.Results;
using LupiraContactApi.Core.Auth;
using LupiraContactApi.Core.Domain.AddressBooks;
using LupiraContactApi.Core.Domain.ContactGroups;
using LupiraContactApi.Core.Domain.Contacts;
using LupiraContactApi.Core.Domain.Identity;
using LupiraContactApi.Core.Domain.Shared;
using LupiraContactApi.Core.Dtos.AddressBooks;
using Marten;

namespace LupiraContactApi.Core.Application;

/// <summary>Lists and creates the address books a principal can access, and shares them by granting/revoking
/// co-owners. Creation grants the caller <c>owner</c>; sharing is owner-only and targets a member by email.</summary>
public sealed class AddressBookService(IDocumentSession session, PrincipalDirectory principals, AccessResolver access, ContactService contacts)
{
    public async Task<OpResult<List<AddressBookDto>>> ListAsync(Guid principalId, CancellationToken ct = default)
    {
        var owners = await session.Query<AddressBookOwner>().Where(o => o.PrincipalId == principalId).ToListAsync(ct);
        var ids = owners.Select(o => o.AddressBookId).ToList();
        var books = await session.Query<AddressBook>().Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        var accessById = owners.ToDictionary(o => o.AddressBookId, o => o.Access);
        return OpResult<List<AddressBookDto>>.Ok([.. books.Select(b => ToDto(b, accessById[b.Id], principalId))]);
    }

    public async Task<OpResult<AddressBookDto>> CreateAsync(Guid principalId, CreateAddressBookRequest r, CancellationToken ct = default)
    {
        var b = new AddressBook { Id = Guid.NewGuid(), Slug = r.Slug, DisplayName = r.DisplayName };
        session.Store(b);
        session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(b.Id, principalId), AddressBookId = b.Id, PrincipalId = principalId, Access = Access.Owner });
        await session.SaveChangesAsync(ct);
        return OpResult<AddressBookDto>.Ok(ToDto(b, Access.Owner, principalId));
    }

    /// <summary>Rename an address book / change its display name (owner-only, merge — null keeps the current value).</summary>
    public async Task<OpResult<AddressBookDto>> UpdateAsync(Guid callerId, Guid addressBookId, UpdateAddressBookRequest r, CancellationToken ct = default)
    {
        var book = await session.LoadAsync<AddressBook>(addressBookId, ct);
        if (book is null) return OpResult<AddressBookDto>.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult<AddressBookDto>.Forbidden("Only an owner may update this address book.");

        if (r.Slug is not null)
        {
            var slug = r.Slug.Trim();
            if (slug.Length == 0) return OpResult<AddressBookDto>.Invalid("Slug cannot be blank.");
            book.Slug = slug;
        }

        if (r.DisplayName is not null) book.DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? null : r.DisplayName.Trim();
        session.Store(book);
        await session.SaveChangesAsync(ct);
        return OpResult<AddressBookDto>.Ok(ToDto(book, Access.Owner, callerId));
    }

    /// <summary>Delete an empty address book (owner-only). Refuses a personal book and any book that still holds
    /// live contacts or groups; on success also removes every access grant on the book.</summary>
    public async Task<OpResult> DeleteAsync(Guid callerId, Guid addressBookId, CancellationToken ct = default)
    {
        var book = await session.LoadAsync<AddressBook>(addressBookId, ct);
        if (book is null) return OpResult.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult.Forbidden("Only an owner may delete this address book.");
        if (book.PersonalOf is not null || book.Slug == AddressBook.PersonalSlug) return OpResult.Conflict("The personal address book cannot be deleted.");
        if (await session.Query<Contact>().AnyAsync(c => c.AddressBookId == addressBookId && c.DeletedAt == null, ct))
            return OpResult.Conflict("Address book is not empty: move or delete its contacts first.");
        if (await session.Query<ContactGroup>().AnyAsync(g => g.AddressBookId == addressBookId && g.DeletedAt == null, ct))
            return OpResult.Conflict("Address book is not empty: delete its groups first.");

        foreach (var grant in await session.Query<AddressBookOwner>().Where(o => o.AddressBookId == addressBookId).ToListAsync(ct))
            session.Delete(grant);
        session.Delete(book);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    /// <summary>Ensures the caller has its own personal address book (<see cref="EnsurePersonalBookAsync"/>) and a linked contact of
    /// its own (<see cref="ContactService.EnsureSelfContactAsync"/>); idempotent. Returns every accessible book.</summary>
    public async Task<OpResult<List<AddressBookDto>>> BootstrapPersonalAsync(Guid principalId, CancellationToken ct = default)
    {
        await EnsurePersonalBookAsync(principalId, ct);
        await contacts.EnsureSelfContactAsync(principalId, ct);
        return await ListAsync(principalId, ct);
    }

    /// <summary>The caller's own personal book: the one it is <see cref="AddressBook.PersonalOf"/>, else an unclaimed <c>personal</c>
    /// book it alone owns (one predating the field, claimed here), else a new one. Another member's shared personal book never counts.</summary>
    public async Task<Guid> EnsurePersonalBookAsync(Guid principalId, CancellationToken ct = default)
    {
        // One read: split in two, a racing bootstrap's claim landing in between would look neither claimed nor claimable.
        var accessible = await access.AccessibleAddressBookIdsAsync(principalId, ct);
        var books = await session.Query<AddressBook>()
            .Where(b => b.PersonalOf == principalId || (b.PersonalOf == null && b.Slug == AddressBook.PersonalSlug && accessible.Contains(b.Id)))
            .OrderBy(b => b.Id).ToListAsync(ct);
        if (books.FirstOrDefault(b => b.PersonalOf == principalId) is { } own) return own.Id;

        var book = await SoleOwnedAsync(principalId, books, ct);
        if (book is null)
        {
            // Deterministic id, so racing bootstraps upsert one book rather than each creating their own.
            book = new AddressBook { Id = DeterministicGuid.From($"personal-{principalId:N}"), Slug = AddressBook.PersonalSlug, DisplayName = "Personal" };
            session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(book.Id, principalId), AddressBookId = book.Id, PrincipalId = principalId, Access = Access.Owner });
        }

        book.PersonalOf = principalId;
        session.Store(book);
        await session.SaveChangesAsync(ct);
        return book.Id;
    }

    // Exactly one candidate whose only owner is the caller — a personal book shared at owner level can't be told from the sharer's.
    private async Task<AddressBook?> SoleOwnedAsync(Guid principalId, IReadOnlyList<AddressBook> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0) return null;
        var ids = candidates.Select(b => b.Id).ToList();
        var owners = await session.Query<AddressBookOwner>().Where(o => ids.Contains(o.AddressBookId) && o.Access == Access.Owner).ToListAsync(ct);
        var sole = candidates.Where(b => owners.Where(o => o.AddressBookId == b.Id).Select(o => o.PrincipalId).ToList() is [var only] && only == principalId).ToList();
        return sole is [var book] ? book : null;
    }

    public async Task<OpResult<OwnerGrantDto>> GrantOwnerAsync(Guid callerId, Guid addressBookId, GrantOwnerRequest r, CancellationToken ct = default)
    {
        if (await session.LoadAsync<AddressBook>(addressBookId, ct) is null) return OpResult<OwnerGrantDto>.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult<OwnerGrantDto>.Forbidden("Only an owner may grant access.");
        var email = (r.Email ?? string.Empty).Trim();
        if (email.Length == 0) return OpResult<OwnerGrantDto>.Invalid("Email is required.");
        var (ok, level) = AccessParsing.Parse(r.Access);
        if (!ok) return OpResult<OwnerGrantDto>.Invalid("Access must be owner, read-write, or read.");

        var target = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        // Deterministic id → re-granting upserts the access level instead of duplicating the grant.
        session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(addressBookId, target.Id), AddressBookId = addressBookId, PrincipalId = target.Id, Access = level });
        await session.SaveChangesAsync(ct);
        return OpResult<OwnerGrantDto>.Ok(new OwnerGrantDto { ContainerId = addressBookId, PrincipalId = target.Id, Email = target.Email, DisplayName = target.DisplayName, Access = level });
    }

    public async Task<OpResult> RevokeOwnerAsync(Guid callerId, Guid addressBookId, string email, CancellationToken ct = default)
    {
        if (await session.LoadAsync<AddressBook>(addressBookId, ct) is null) return OpResult.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult.Forbidden("Only an owner may revoke access.");
        var target = await principals.FindByEmailAsync(email, ct);
        if (target is null) return OpResult.NotFound();

        var grants = await session.Query<AddressBookOwner>().Where(o => o.AddressBookId == addressBookId).ToListAsync(ct);
        var targetGrant = grants.FirstOrDefault(o => o.PrincipalId == target.Id);
        if (targetGrant is null) return OpResult.NotFound();
        if (OwnerGrants.WouldOrphan(targetGrant.Access, [.. grants.Where(o => o.PrincipalId != target.Id).Select(o => o.Access)]))
            return OpResult.Conflict("Cannot remove the last owner.");

        session.Delete(targetGrant);
        await session.SaveChangesAsync(ct);
        return OpResult.Ok();
    }

    /// <summary>Lists who has access to an address book and at what level (owner-only). Fills the read side of the sharing panel.</summary>
    public async Task<OpResult<List<OwnerGrantDto>>> ListOwnersAsync(Guid callerId, Guid addressBookId, CancellationToken ct = default)
    {
        if (await session.LoadAsync<AddressBook>(addressBookId, ct) is null) return OpResult<List<OwnerGrantDto>>.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult<List<OwnerGrantDto>>.Forbidden("Only an owner may list access grants.");

        var grants = await session.Query<AddressBookOwner>().Where(o => o.AddressBookId == addressBookId).ToListAsync(ct);
        var byId = (await session.LoadManyAsync<Principal>(ct, grants.Select(g => g.PrincipalId).Distinct().ToArray())).ToDictionary(p => p.Id);
        return OpResult<List<OwnerGrantDto>>.Ok([.. grants
            .Select(g =>
            {
                byId.TryGetValue(g.PrincipalId, out var p);
                return new OwnerGrantDto { ContainerId = addressBookId, PrincipalId = g.PrincipalId, Email = p?.Email ?? string.Empty, DisplayName = p?.DisplayName, Access = g.Access };
            })
            .OrderByDescending(o => o.Access == Access.Owner).ThenBy(o => o.Email, StringComparer.OrdinalIgnoreCase)]);
    }

    private static AddressBookDto ToDto(AddressBook b, Access access, Guid principalId) =>
        new() { Id = b.Id, Slug = b.Slug, DisplayName = b.DisplayName, Access = access, IsPersonal = b.PersonalOf == principalId };
}
