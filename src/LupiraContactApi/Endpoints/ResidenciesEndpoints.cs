using LupiraContactApi.Core.Dtos.Residencies;
using LupiraContactApi.Handlers;
using Microsoft.AspNetCore.Mvc;

namespace LupiraContactApi.Endpoints;

public static class ResidenciesEndpoints
{
    public static IEndpointRouteBuilder MapResidencies(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(string.Empty).RequireAuthorization("ApiPolicy").WithTags("Residencies");

        group.MapGet("/contacts/{id:guid}/residencies", (Guid id, ResidenciesHandler h, CancellationToken ct) => h.ListAsync(id, ct))
            .WithName("ListContactResidencies")
            .WithSummary("Where the contact lives, holidays and works: current residencies first, then the most recent move-in first.")
            .Produces<List<ResidencyDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/contacts/{id:guid}/residencies", (Guid id, ResidencyRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ResidenciesHandler h, CancellationToken ct) => h.AddAsync(id, body, idempotencyKey, ct))
            .WithName("AddResidency")
            .WithSummary("Start a residency at a LupiraGeoApi place (resolve the address there first — no free text). Refused when it overlaps another of the contact's residencies at the same place.")
            .Produces<ResidencyDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/residencies/{id:guid}", (Guid id, ResidencyRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ResidenciesHandler h, CancellationToken ct) => h.ReviseAsync(id, body, idempotencyKey, ct))
            .WithName("ReviseResidency")
            .WithSummary("Correct a residency as entered: place, type, label and period, wholesale. A move is told with move-out or POST /moves instead.")
            .Produces<ResidencyDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/residencies/{id:guid}/move-out", (Guid id, MoveOutRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ResidenciesHandler h, CancellationToken ct) => h.MoveOutAsync(id, body, idempotencyKey, ct))
            .WithName("MoveOutOfResidency")
            .WithSummary("End a residency: the contact moved out on the given date (a year, year-month, or day).")
            .Produces<ResidencyDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/residencies/{id:guid}", (Guid id, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ResidenciesHandler h, CancellationToken ct) => h.RemoveAsync(id, idempotencyKey, ct))
            .WithName("RemoveResidency")
            .WithSummary("Erase a residency entered by mistake. One that ended should be moved out of instead.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/residencies", (ResidenciesHandler h, CancellationToken ct) => h.ListVisibleAsync(ct))
            .WithName("ListResidencies")
            .WithSummary("Every residency of a contact the caller can read — who lives, holidays and works where.")
            .Produces<List<ResidencyDto>>(StatusCodes.Status200OK);

        group.MapPost("/moves", (MoveRequest body, [FromHeader(Name = "Idempotency-Key")] Guid? idempotencyKey, ResidenciesHandler h, CancellationToken ct) => h.MoveAsync(body, idempotencyKey, ct))
            .WithName("RecordMove")
            .WithSummary("Several contacts move together: each one's current residencies at fromPlaceId end on movedIn, and a residency at toPlaceId starts then. All or nothing. Returns the new residencies.")
            .Produces<List<ResidencyDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
