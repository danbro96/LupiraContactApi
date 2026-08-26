using LupiraContactApi.Dtos.Internal;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

/// <summary>Service-to-service seams (LAN-only: not tunneled + CF-header backstop). Excluded from the public OpenAPI document.</summary>
public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternal(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/contacts/resolve",
                (ResolveContactsRequest body, InternalContactsHandler h, CancellationToken ct) => h.ResolveAsync(body, ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription();
        app.MapGet("/internal/contacts/birthdays",
                (InternalContactsHandler h, CancellationToken ct) => h.BirthdaysAsync(ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription();
        app.MapPost("/internal/contacts/place-references:check",
                (CheckPlaceReferencesRequest body, InternalContactsHandler h, CancellationToken ct) => h.CheckPlaceReferencesAsync(body, ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription();
        return app;
    }
}
