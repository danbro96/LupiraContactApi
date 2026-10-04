using LupiraContactApi.Auth;
using LupiraContactApi.Core.Application;
using LupiraContactApi.Core.Dtos.Relationships;
using Microsoft.AspNetCore.Http.HttpResults;

namespace LupiraContactApi.Handlers;

public sealed class RelationshipsHandler(CurrentUser user, RelationshipFeed feed)
{
    public async Task<Results<Ok<List<RelationshipDto>>, UnauthorizedHttpResult>> ListAsync(CancellationToken ct)
    {
        var u = await user.GetAsync(ct);
        return TypedResults.Ok((await feed.VisibleAsync(u.Id, ct)).ToList());
    }
}
