using LupiraContactApi.Core.Domain;
using Marten;

namespace LupiraContactApi.Core.Auth;

/// <summary>Container-scoped authorization over the multi-owner membership docs: a principal may read an address book it
/// has any grant on, and write one it owns or has a read-write grant on.</summary>
public sealed class AccessResolver(IQuerySession session)
{
    public async Task<List<Guid>> AccessibleAddressBookIdsAsync(Guid principalId, CancellationToken ct = default) =>
        await session.Query<AddressBookOwner>().Where(o => o.PrincipalId == principalId).Select(o => o.AddressBookId).ToListAsync(ct) is { } l ? [.. l] : [];

    /// <summary>Owner-only (not read-write): gates granting/revoking co-owners on a container.</summary>
    public async Task<bool> IsAddressBookOwnerAsync(Guid principalId, Guid addressBookId, CancellationToken ct = default) =>
        await session.Query<AddressBookOwner>().AnyAsync(o => o.AddressBookId == addressBookId && o.PrincipalId == principalId && o.Access == Access.Owner, ct);

    public async Task<bool> CanReadAddressBookAsync(Guid principalId, Guid addressBookId, CancellationToken ct = default) =>
        await session.Query<AddressBookOwner>().AnyAsync(o => o.AddressBookId == addressBookId && o.PrincipalId == principalId, ct);

    public async Task<bool> CanWriteAddressBookAsync(Guid principalId, Guid addressBookId, CancellationToken ct = default) =>
        await session.Query<AddressBookOwner>().AnyAsync(
            o => o.AddressBookId == addressBookId && o.PrincipalId == principalId && (o.Access == Access.Owner || o.Access == Access.ReadWrite), ct);
}
