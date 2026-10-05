using Lupira.Sync;
using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Contacts;
using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Core.Dtos.Relationships;
using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

public static class SyncEndpoints
{
    private const string Paging = " Omit since for a full sync; loop while hasMore, persisting cursor between calls. reset = drop the local mirror first.";

    public static IEndpointRouteBuilder MapSync(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync").RequireAuthorization("ApiPolicy").WithTags("Sync");

        group.MapGet("/contacts", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.ContactsAsync(since, limit, ct))
            .WithName("GetContactChanges")
            .WithSummary("Contacts for offline mirrors: every contact the caller can read that changed since the cursor, with its section guards, plus tombstone ids for contacts deleted or no longer readable (incl. moved to an unreadable address book)." + Paging)
            .Produces<SyncPage<ContactSyncChange>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/changes", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.ContactsAsync(since, limit, ct))
            .WithName("GetChanges")
            .WithSummary("Same feed as GET /sync/contacts, kept for existing installs.")
            .Produces<SyncPage<ContactSyncChange>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/relationships", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.RelationshipsAsync(since, limit, ct))
            .WithName("GetRelationshipChanges")
            .WithSummary("Relationships for offline mirrors: those whose two contacts the caller can read that changed since the cursor (directly, or through a contact on them), plus tombstone ids for removed or no-longer-visible ones." + Paging)
            .Produces<SyncPage<RelationshipDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/residencies", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.ResidenciesAsync(since, limit, ct))
            .WithName("GetResidencyChanges")
            .WithSummary("Residencies for offline mirrors: those of contacts the caller can read that changed since the cursor (directly, or through their contact), plus tombstone ids for removed or no-longer-visible ones." + Paging)
            .Produces<SyncPage<ResidencyDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/place-entries", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.PlaceEntriesAsync(since, limit, ct))
            .WithName("GetPlaceEntryChanges")
            .WithSummary("Door and gate codes for offline mirrors, per place the caller can see a current resident of. Tombstones are place ids no longer visible." + Paging)
            .Produces<SyncPage<PlaceEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/address-books", (SyncHandler h, CancellationToken ct) => h.AddressBooksAsync(ct))
            .WithName("GetAddressBookSnapshot")
            .WithSummary("Every address book the caller can read, with their access level. Always a complete snapshot (reset, no cursor): replace the local copy on each call.")
            .Produces<SyncPage<AddressBookDto>>(StatusCodes.Status200OK);

        group.MapGet("/groups", (SyncHandler h, CancellationToken ct) => h.GroupsAsync(ct))
            .WithName("GetContactGroupSnapshot")
            .WithSummary("Every contact group in an address book the caller can read. Always a complete snapshot (reset, no cursor): replace the local copy on each call.")
            .Produces<SyncPage<ContactGroupDto>>(StatusCodes.Status200OK);

        group.MapGet("/containers", (SyncHandler h, CancellationToken ct) => h.ContainersAsync(ct))
            .WithName("GetSyncContainers")
            .WithSummary("Snapshot of the caller's address books + contact groups for mirror reconciliation (no cursor — fetch once per sync cycle and diff locally).")
            .Produces<SyncContainersResponse>(StatusCodes.Status200OK);

        return app;
    }
}
