using LupiraContactApi.Core.Dtos.Relationships;
using LupiraContactApi.Handlers;

namespace LupiraContactApi.Endpoints;

public static class RelationshipsEndpoints
{
    public static IEndpointRouteBuilder MapRelationships(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/relationships").RequireAuthorization("ApiPolicy").WithTags("Relationships");

        group.MapGet(string.Empty, (RelationshipsHandler h, CancellationToken ct) => h.ListAsync(ct))
            .WithName("ListRelationships")
            .WithSummary("Every relationship whose two contacts the caller can read. Per contact, GET /contacts/{id}/relations gives the view from that contact.")
            .Produces<List<RelationshipDto>>(StatusCodes.Status200OK);

        return app;
    }
}
