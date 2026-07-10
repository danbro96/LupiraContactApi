using LupiraContactApi.Auth;
using LupiraContactApi.Domain;
using LupiraContactApi.Dtos.AddressBooks;
using Marten;

namespace LupiraContactApi.Application;

/// <summary>Lists and creates the address books a principal can access, and shares them by granting/revoking
/// co-owners. Creation grants the caller <c>owner</c>; sharing is owner-only and targets a member by email.</summary>
public sealed class AddressBookService(IDocumentSession session, PrincipalDirectory principals, AccessResolver access)
{
    public async Task<OpResult<List<AddressBookDto>>> ListAsync(Guid principalId, CancellationToken ct = default)
    {
        var owners = await session.Query<AddressBookOwner>().Where(o => o.PrincipalId == principalId).ToListAsync(ct);
        var ids = owners.Select(o => o.AddressBookId).ToList();
        var books = await session.Query<AddressBook>().Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        var accessById = owners.ToDictionary(o => o.AddressBookId, o => o.Access);
        return OpResult<List<AddressBookDto>>.Ok(
            [.. books.Select(b => new AddressBookDto { Id = b.Id, Slug = b.Slug, DisplayName = b.DisplayName, Access = accessById[b.Id] })]);
    }

    public async Task<OpResult<AddressBookDto>> CreateAsync(Guid principalId, CreateAddressBookRequest r, CancellationToken ct = default)
    {
        var b = new AddressBook { Id = Guid.NewGuid(), Slug = r.Slug, DisplayName = r.DisplayName };
        session.Store(b);
        session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(b.Id, principalId), AddressBookId = b.Id, PrincipalId = principalId, Access = Access.Owner });
        await session.SaveChangesAsync(ct);
        return OpResult<AddressBookDto>.Ok(new AddressBookDto { Id = b.Id, Slug = b.Slug, DisplayName = b.DisplayName, Access = Access.Owner });
    }

    /// <summary>Ensures the caller has a <c>personal</c> address book; idempotent — matched on slug, so a second call creates nothing.</summary>
    public async Task<OpResult<List<AddressBookDto>>> BootstrapPersonalAsync(Guid principalId, CancellationToken ct = default)
    {
        var existing = (await ListAsync(principalId, ct)).Value!;
        if (!existing.Any(b => b.Slug == "personal"))
            existing.Add((await CreateAsync(principalId, new CreateAddressBookRequest { Slug = "personal", DisplayName = "Personal" }, ct)).Value!);
        return OpResult<List<AddressBookDto>>.Ok(existing);
    }

    public async Task<OpResult<OwnerGrantDto>> GrantOwnerAsync(Guid callerId, Guid addressBookId, GrantOwnerRequest r, CancellationToken ct = default)
    {
        if (await session.LoadAsync<AddressBook>(addressBookId, ct) is null) return OpResult<OwnerGrantDto>.NotFound();
        if (!await access.IsAddressBookOwnerAsync(callerId, addressBookId, ct)) return OpResult<OwnerGrantDto>.Forbidden("Only an owner may grant access.");
        var email = (r.Email ?? "").Trim();
        if (email.Length == 0) return OpResult<OwnerGrantDto>.Invalid("Email is required.");
        var (ok, level) = AccessParsing.Parse(r.Access);
        if (!ok) return OpResult<OwnerGrantDto>.Invalid("Access must be owner, read-write, or read.");

        var target = await principals.ResolveOrProvisionAsync(null, email, null, ct);
        // Deterministic id → re-granting upserts the access level instead of duplicating the grant.
        session.Store(new AddressBookOwner { Id = AddressBookOwner.MakeId(addressBookId, target.Id), AddressBookId = addressBookId, PrincipalId = target.Id, Access = level });
        await session.SaveChangesAsync(ct);
        return OpResult<OwnerGrantDto>.Ok(new OwnerGrantDto { ContainerId = addressBookId, PrincipalId = target.Id, Email = target.Email, Access = level });
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
}
