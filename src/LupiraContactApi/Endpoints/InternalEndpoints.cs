using LupiraContactApi.Core.Dtos.Internal;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

/// <summary>Service-to-service seams (LAN-only: not tunneled + CF-header backstop). Excluded from the public OpenAPI document.</summary>
public static class InternalEndpoints
{
    public static IEndpointRouteBuilder MapInternal(this IEndpointRouteBuilder app)
    {
        app.MapPost(
            "/internal/contacts/resolve",
                (ResolveContactsRequest body, InternalContactsHandler h, CancellationToken ct) => h.ResolveAsync(body, ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription()
            .WithName("ResolveContact");
        app.MapPost(
            "/internal/contacts/describe",
                (DescribeContactsRequest body, InternalContactsHandler h, CancellationToken ct) => h.DescribeAsync(body, ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription()
            .WithName("DescribeContacts");
        app.MapGet(
            "/internal/contacts/birthdays",
                (InternalContactsHandler h, CancellationToken ct) => h.BirthdaysAsync(ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription()
            .WithName("ListBirthdays");
        app.MapPost(
            "/internal/contacts/place-references:check",
                (CheckPlaceReferencesRequest body, InternalContactsHandler h, CancellationToken ct) => h.CheckPlaceReferencesAsync(body, ct))
            .RequireAuthorization("InternalPolicy")
            .ExcludeFromDescription()
            .WithName("CheckPlaceReferences");
        return app;
    }
}
