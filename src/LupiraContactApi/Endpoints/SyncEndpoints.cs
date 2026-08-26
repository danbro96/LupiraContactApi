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

        group.MapGet("/containers", (SyncHandler h, CancellationToken ct) => h.ContainersAsync(ct))
            .WithName("GetSyncContainers")
            .WithSummary("Snapshot of the caller's address books + contact groups for mirror reconciliation (no cursor — fetch once per sync cycle and diff locally).")
            .Produces<SyncContainersResponse>(StatusCodes.Status200OK);

        return app;
    }
}
