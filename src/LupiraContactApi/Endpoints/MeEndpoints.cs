using LupiraContactApi.Core.Dtos.AddressBooks;
using LupiraContactApi.Core.Dtos.Me;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMe(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", (MeHandler h, CancellationToken ct) => h.GetAsync(ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithName("GetMe")
            .WithSummary("The caller's resolved local identity (JIT-provisioned on first login).")
            .Produces<MeDto>(StatusCodes.Status200OK);

        app.MapPost("/me/bootstrap", (MeHandler h, CancellationToken ct) => h.BootstrapAsync(ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithName("BootstrapMe")
            .WithSummary("Idempotently ensure the caller has a personal address book; returns all accessible books.")
            .Produces<List<AddressBookDto>>(StatusCodes.Status200OK);

        app.MapPut("/me/contact", (SetMyContactRequest body, MeHandler h, CancellationToken ct) => h.SetContactAsync(body, ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithName("SetMyContact")
            .WithSummary("Link the caller's identity to its own contact (\"this card is me\") — the default focus for contact circles.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        return app;
    }
}
