using LupiraContactApi.Core.Dtos.Sync;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSync(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/sync").RequireAuthorization("ApiPolicy").WithTags("Sync");

        group.MapGet("/changes", (string? since, int? limit, SyncHandler h, CancellationToken ct) => h.ChangesAsync(since, limit, ct))
            .WithName("GetChanges")
            .WithSummary("Delta feed for offline mirrors: every contact the caller can read that changed past the cursor, plus tombstone ids for contacts deleted or no longer visible (incl. moved to an unreadable address book). Omit since for a full sync; loop while hasMore, persisting cursor between calls.")
            .Produces<SyncChangesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/relationships", (string? since, SyncHandler h, CancellationToken ct) => h.RelationshipsAsync(since, ct))
            .WithName("GetRelationshipChanges")
            .WithSummary("Delta feed of relationships for offline mirrors: those whose two contacts the caller can read that changed past the cursor (directly, or through a contact on them), plus tombstone ids for removed or no-longer-visible ones. Unpaged. Omit since for a full sync.")
            .Produces<RelationshipChangesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/residencies", (string? since, SyncHandler h, CancellationToken ct) => h.ResidenciesAsync(since, ct))
            .WithName("GetResidencyChanges")
            .WithSummary("Delta feed of residencies for offline mirrors: those of contacts the caller can read that changed past the cursor (directly, or through their contact), plus tombstone ids for removed or no-longer-visible ones. Unpaged. Omit since for a full sync.")
            .Produces<ResidencyChangesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/place-entries", (string? since, SyncHandler h, CancellationToken ct) => h.PlaceEntriesAsync(since, ct))
            .WithName("GetPlaceEntryChanges")
            .WithSummary("Delta feed of door and gate codes for offline mirrors, per place the caller can see a current resident of. Tombstones are place ids no longer visible. Unpaged. Omit since for a full sync.")
            .Produces<PlaceEntryChangesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/containers", (SyncHandler h, CancellationToken ct) => h.ContainersAsync(ct))
            .WithName("GetSyncContainers")
            .WithSummary("Snapshot of the caller's address books + contact groups for mirror reconciliation (no cursor — fetch once per sync cycle and diff locally).")
            .Produces<SyncContainersResponse>(StatusCodes.Status200OK);

        return app;
    }
}
