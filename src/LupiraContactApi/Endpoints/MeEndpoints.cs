using LupiraContactApi.Dtos.AddressBooks;
using LupiraContactApi.Dtos.Me;
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
            .Produces<MeDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost("/me/bootstrap", (MeHandler h, CancellationToken ct) => h.BootstrapAsync(ct))
            .RequireAuthorization("ApiPolicy")
            .WithTags("Me")
            .WithName("BootstrapMe")
            .WithSummary("Idempotently ensure the caller has a personal address book; returns all accessible books.")
            .Produces<List<AddressBookDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }
}
