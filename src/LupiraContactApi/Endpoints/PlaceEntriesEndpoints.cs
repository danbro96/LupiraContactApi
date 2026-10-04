using LupiraContactApi.Core.Dtos.PlaceEntries;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

public static class PlaceEntriesEndpoints
{
    public static IEndpointRouteBuilder MapPlaceEntries(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/places/{placeId:guid}").RequireAuthorization("ApiPolicy").WithTags("PlaceEntries");

        group.MapGet("/entry", (Guid placeId, PlaceEntriesHandler h, CancellationToken ct) => h.GetAsync(placeId, ct))
            .WithName("GetPlaceEntry")
            .WithSummary("The door and gate codes at a place. Visible while the caller can read a contact currently living there; otherwise 404.")
            .Produces<PlaceEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/entry-codes/{codeId:guid}", (Guid placeId, Guid codeId, SetEntryCodeRequest body, PlaceEntriesHandler h, CancellationToken ct) => h.SetCodeAsync(placeId, codeId, body, ct))
            .WithName("SetEntryCode")
            .WithSummary("Add or replace a code (the client mints codeId). Needs write on a contact currently living there.")
            .Produces<PlaceEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/entry-codes/{codeId:guid}", (Guid placeId, Guid codeId, PlaceEntriesHandler h, CancellationToken ct) => h.RemoveCodeAsync(placeId, codeId, ct))
            .WithName("RemoveEntryCode")
            .WithSummary("Remove a code. Needs write on a contact currently living there.")
            .Produces<PlaceEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
